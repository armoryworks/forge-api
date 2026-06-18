using System.Security.Claims;
using System.Text.Json;

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
        RuleFor(x => x.Method).NotEmpty()
            // Constrain to the PaymentMethod enum so an invalid string is a 400, not a 500 from the
            // handler's Enum.Parse (parity with CreateVendorPayment — Phase-2 review).
            .Must(m => Enum.TryParse<PaymentMethod>(m, ignoreCase: true, out _))
            .WithMessage("Method must be one of: Cash, Check, CreditCard, BankTransfer, Wire, Other");
        When(x => x.Applications != null && x.Applications.Count > 0, () =>
        {
            RuleFor(x => x.Applications!.Sum(a => a.Amount))
                .LessThanOrEqualTo(x => x.Amount)
                .WithMessage("Applied amounts cannot exceed payment amount");
            // A duplicate invoice reference would bypass the per-invoice over-apply guard (EF returns the
            // same tracked entity, so each check sees the same pre-application balance) — reject duplicates.
            RuleFor(x => x.Applications!)
                .Must(a => a.Select(x => x.InvoiceId).Distinct().Count() == a.Count)
                .WithMessage("An invoice may be referenced at most once per payment");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.InvoiceId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
                // Settlement FX rate must be positive (a 0 or negative rate would zero/invert the functional
                // cash amount, breaking the realized-FX plug). Default 1 satisfies this for single-currency.
                app.RuleFor(a => a.SettlementFxRate).GreaterThan(0m);
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
    IHttpContextAccessor? httpContextAccessor = null,
    // AUDIT-21-S1: same optional/null-default pattern — DI supplies both; the QBO enqueue self-gates
    // on a provider being connected.
    IAccountingService? accountingService = null,
    ISyncQueueRepository? syncQueue = null)
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
        // SOs whose invoice(s) became fully paid in this payment — completion candidates.
        var completionCandidateSoIds = new HashSet<int>();

        if (request.Applications != null)
        {
            foreach (var app in request.Applications)
            {
                var invoice = await invoiceRepo.FindWithDetailsAsync(app.InvoiceId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Invoice {app.InvoiceId} not found");

                // Sub-ledger integrity: the payment's customer must own the invoice (parity with
                // CreateVendorPayment — Phase-2 review).
                if (invoice.CustomerId != request.CustomerId)
                    throw new InvalidOperationException(
                        $"Invoice {invoice.InvoiceNumber} belongs to a different customer");

                // Only a finalized (AR-booked) invoice can be paid. A Draft invoice has not posted its AR
                // debit (posting fires on SendInvoice), so paying it would Cr AR against a receivable the
                // GL never recorded; Paid/Voided are terminal. Restrict to Sent / PartiallyPaid / Overdue.
                if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Overdue))
                    throw new InvalidOperationException(
                        $"Invoice {invoice.InvoiceNumber} is {invoice.Status}; only Sent, PartiallyPaid, or Overdue invoices can be paid");

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
                    SettlementFxRate = app.SettlementFxRate,
                });

                appliedTotal += app.Amount;

                // Update invoice status
                var newBalance = balanceDue - app.Amount;
                if (newBalance <= 0)
                {
                    invoice.Status = InvoiceStatus.Paid;
                    if (invoice.SalesOrderId is int paidSoId) completionCandidateSoIds.Add(paidSoId);
                }
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

        // ── Order-to-cash completion. An order that is fully shipped with all its
        // (non-voided) invoices paid is done — advance it to Completed, the terminal
        // O2C state the prior pipeline never set (CreateShipment only reaches Shipped).
        // Runs after the invoice-Paid flush so the statuses checked here are committed.
        if (completionCandidateSoIds.Count > 0)
        {
            var anyCompleted = false;
            foreach (var soId in completionCandidateSoIds)
            {
                var so = await db.SalesOrders
                    .Include(o => o.Lines)
                    .Include(o => o.Invoices)
                    .FirstOrDefaultAsync(o => o.Id == soId, cancellationToken);
                if (so is null || so.Status != SalesOrderStatus.Shipped) continue;
                if (so.Lines.Count == 0 || !so.Lines.All(l => l.IsFullyShipped)) continue;
                var billable = so.Invoices.Where(i => i.Status != InvoiceStatus.Voided).ToList();
                if (billable.Count == 0 || !billable.All(i => i.Status == InvoiceStatus.Paid)) continue;
                so.Status = SalesOrderStatus.Completed;
                anyCompleted = true;
            }
            if (anyCompleted) await db.SaveChangesAsync(cancellationToken);
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

        // AUDIT-21-S1 (BLOCKER): enqueue a QBO payment sync row so AR cash receipts reach the
        // accounting provider — before this only MoveJobStage enqueued, leaving payments invisible to
        // sync. Inside the open transaction so the sync row commits/rolls back with the payment.
        if (accountingService is not null && syncQueue is not null
            && await accountingService.TestConnectionAsync(cancellationToken))
        {
            var accountingPayment = new AccountingPayment(
                ExternalId: customer.ExternalId ?? string.Empty,
                Amount: payment.Amount,
                Date: payment.PaymentDate,
                Method: payment.Method.ToString());
            await syncQueue.EnqueueAsync("Payment", payment.Id, "CreatePayment",
                JsonSerializer.Serialize(accountingPayment), cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, customer.Name,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
