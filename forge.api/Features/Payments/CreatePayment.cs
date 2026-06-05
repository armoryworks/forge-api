using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Payments;

public record CreatePaymentCommand(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications) : IRequest<PaymentListItemModel>;

public class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).NotEmpty();
        When(x => x.Applications != null && x.Applications.Count > 0, () =>
        {
            RuleFor(x => x.Applications!.Sum(a => a.Amount))
                .LessThanOrEqualTo(x => x.Amount)
                .WithMessage("Applied amounts cannot exceed payment amount");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.InvoiceId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
            });
        });
    }
}

public class CreatePaymentHandler(
    IPaymentRepository repo,
    ICustomerRepository customerRepo,
    IInvoiceRepository invoiceRepo,
    AppDbContext db,
    // Optional / null-default so the handler stays constructible without an
    // accounting context (e.g. isolated unit tests). The production DI path
    // supplies both; with CAP-ACCT-FULLGL off the posting service no-ops anyway
    // (mirrors SendInvoice's STAGE A wiring).
    IPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<CreatePaymentCommand, PaymentListItemModel>
{
    public async Task<PaymentListItemModel> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var customer = await customerRepo.FindAsync(request.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {request.CustomerId} not found");

        // One unit of work: the payment + its applications + the invoice-status
        // updates AND the inline cash-receipt posting commit (or roll back) together
        // — the locked inline, single-transaction model (§2). The engine's
        // SaveChanges joins this transaction instead of committing on its own, so a
        // posting failure unwinds the payment too (no orphaned operational row). On
        // Npgsql this is a real transaction; on the in-memory test provider it's an
        // ignored no-op, so the mock-based handler tests are unaffected.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var paymentNumber = await repo.GenerateNextPaymentNumberAsync(cancellationToken);
        var method = Enum.Parse<PaymentMethod>(request.Method, true);

        var payment = new Payment
        {
            PaymentNumber = paymentNumber,
            CustomerId = request.CustomerId,
            Method = method,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
        };

        decimal appliedTotal = 0;

        if (request.Applications != null)
        {
            foreach (var app in request.Applications)
            {
                var invoice = await invoiceRepo.FindWithDetailsAsync(app.InvoiceId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Invoice {app.InvoiceId} not found");

                // F-027: consume the canonical Invoice.BalanceDue rather than re-deriving the
                // money formula here. The two were numerically equal only while
                // InvoiceLine.LineTotal == Quantity * UnitPrice; a single source of truth keeps
                // payment validation and the invoice's reported balance from drifting apart.
                var balanceDue = invoice.BalanceDue;

                if (app.Amount > balanceDue)
                    throw new InvalidOperationException(
                        $"Application amount {app.Amount:C} exceeds invoice {invoice.InvoiceNumber} balance of {balanceDue:C}");

                payment.Applications.Add(new PaymentApplication
                {
                    InvoiceId = app.InvoiceId,
                    Amount = app.Amount,
                });

                appliedTotal += app.Amount;

                // Update invoice status
                var newBalance = balanceDue - app.Amount;
                if (newBalance <= 0)
                    invoice.Status = InvoiceStatus.Paid;
                else if (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Overdue)
                    invoice.Status = InvoiceStatus.PartiallyPaid;

                // F-026: force the concurrency token modified so a concurrent SaveChanges on
                // the same invoice row collides and throws DbUpdateConcurrencyException.
                db.Entry(invoice).Property(i => i.Version).IsModified = true;
            }
        }

        await repo.AddAsync(payment, cancellationToken);

        try
        {
            await repo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // A concurrent payment committed to this invoice between our read and write.
            // Reload each conflicted invoice (including navigations for balance calc) and
            // re-run the over-apply guard; throws InvalidOperationException → 409.
            foreach (var entry in ex.Entries.Where(e => e.Entity is Invoice))
            {
                await entry.ReloadAsync(cancellationToken);
                var inv = (Invoice)entry.Entity;
                await db.Entry(inv).Collection(i => i.PaymentApplications).LoadAsync(cancellationToken);
                await db.Entry(inv).Collection(i => i.Lines).LoadAsync(cancellationToken);

                var freshBalance = inv.BalanceDue;

                var matchingApp = request.Applications!.First(a => a.InvoiceId == inv.Id);
                if (matchingApp.Amount > freshBalance)
                    throw new InvalidOperationException(
                        $"Application amount {matchingApp.Amount:C} exceeds invoice {inv.InvoiceNumber} balance of {freshBalance:C}");
            }

            throw new InvalidOperationException("Concurrent payment conflict — please retry.");
        }

        // ── Inline cash-receipt posting (Phase-1 STAGE B, §7 matrix row "Payment
        // applied"). Runs AFTER the operational SaveChanges (within the same open
        // transaction) so the payment row — and its applications / invoice-status
        // changes — is flushed and the payment Id is assigned for the posting to
        // reference as its source. The engine posts on the SAME request-scoped
        // context and its SaveChanges joins this transaction; nothing commits until
        // the tx.CommitAsync below, so a posting failure rolls the payment back too
        // (the locked inline model — §2). No-op while CAP-ACCT-FULLGL is off; the
        // service self-gates, so the operational payment flow is unchanged while dark.
        if (cashPosting is not null)
        {
            var createdByUserId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : 0;

            await cashPosting.PostPaymentCreatedAsync(payment.Id, createdByUserId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer.Name,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
