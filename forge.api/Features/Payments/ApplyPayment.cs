using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Payments;

public record ApplyPaymentCommand(int PaymentId, List<CreatePaymentApplicationModel> Applications)
    : IRequest<PaymentListItemModel>;

public class ApplyPaymentValidator : AbstractValidator<ApplyPaymentCommand>
{
    public ApplyPaymentValidator()
    {
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.Applications).NotNull().NotEmpty()
            .WithMessage("At least one invoice application is required");
        When(x => x.Applications is { Count: > 0 }, () =>
        {
            RuleFor(x => x.Applications)
                .Must(a => a.Select(x => x.InvoiceId).Distinct().Count() == a.Count)
                .WithMessage("An invoice may be referenced at most once per payment");
            RuleForEach(x => x.Applications).ChildRules(app =>
            {
                app.RuleFor(a => a.InvoiceId).GreaterThan(0);
                app.RuleFor(a => a.Amount).GreaterThan(0);
                app.RuleFor(a => a.SettlementFxRate).GreaterThan(0m);
            });
        });
    }
}

public class ApplyPaymentHandler(
    IPaymentRepository repo,
    IInvoiceRepository invoiceRepo,
    ISettingsService settings,
    AppDbContext db,
    IPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<ApplyPaymentCommand, PaymentListItemModel>
{
    public async Task<PaymentListItemModel> Handle(ApplyPaymentCommand request, CancellationToken cancellationToken)
    {
        var policy = await settings.GetStringAsync(PaymentsSettings.ModificationPolicyKey, cancellationToken)
                     ?? PaymentsSettings.PolicyFull;
        if (policy == PaymentsSettings.PolicyLocked)
            throw new InvalidOperationException("Payment modifications are locked by the payment policy.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync(
                "SELECT id FROM payments WHERE id = {0} FOR UPDATE", [request.PaymentId], cancellationToken);

        var payment = await repo.FindWithDetailsAsync(request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.PaymentId} not found");

        var unapplied = payment.UnappliedAmount;
        var requested = request.Applications.Sum(a => a.Amount);
        if (requested > unapplied)
            throw new InvalidOperationException(
                $"Applied amounts {requested:C} exceed the payment's unapplied balance of {unapplied:C}");

        var existingApplications = payment.Applications.ToHashSet();
        var completionCandidateSoIds = new HashSet<int>();
        foreach (var app in request.Applications)
        {
            var invoice = await invoiceRepo.FindWithDetailsAsync(app.InvoiceId, cancellationToken)
                ?? throw new KeyNotFoundException($"Invoice {app.InvoiceId} not found");

            PaymentApplicationRules.ApplyToInvoice(db, payment, invoice, app, completionCandidateSoIds);

            db.LogActivityAt("applied",
                $"Applied {app.Amount:C} of payment {payment.PaymentNumber} to invoice {invoice.InvoiceNumber}",
                ("Payment", payment.Id), ("Invoice", invoice.Id), ("Customer", payment.CustomerId));
        }

        db.Entry(payment).Property(p => p.Version).IsModified = true;

        try
        {
            await repo.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrent payment conflict — please retry.");
        }

        var newApplicationIds = payment.Applications
            .Where(a => !existingApplications.Contains(a))
            .Select(a => a.Id)
            .ToList();

        var appliedInvoiceIds = request.Applications.Select(a => a.InvoiceId).Distinct().ToList();
        if (await InvoicedMilestonePayments.RefreshAsync(
                db, appliedInvoiceIds, payment.PaymentDate, payment.PaymentNumber, cancellationToken))
            await db.SaveChangesAsync(cancellationToken);

        await PaymentApplicationRules.CompletePaidSalesOrdersAsync(db, completionCandidateSoIds, cancellationToken);

        if (cashPosting is not null)
        {
            var userId =
                int.TryParse(
                    httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var uid)
                    ? uid
                    : db.CurrentUserId ?? 0;

            await cashPosting.PostPaymentApplicationsAsync(payment.Id, newApplicationIds, userId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        var appliedTotal = payment.AppliedAmount;
        return new PaymentListItemModel(
            payment.Id, payment.PaymentNumber, payment.CustomerId, payment.Customer?.Name ?? string.Empty,
            payment.Method.ToString(), payment.Amount, appliedTotal,
            payment.Amount - appliedTotal, payment.PaymentDate,
            payment.ReferenceNumber, payment.CreatedAt);
    }
}
