using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Payments;

// P06-5: void (reverse) a recorded payment — distinct from DeletePayment, which refuses
// when the payment has applications. Void drops the applications so the invoices reopen,
// soft-deletes the payment (lossless — the row + activity trail are preserved), and is
// gated by the admin-selectable payments.modification-policy.
public record VoidPaymentCommand(int Id, VoidPaymentRequestModel Data) : IRequest;

public class VoidPaymentHandler(IPaymentRepository repo, AppDbContext db, ISettingsService settings)
    : IRequestHandler<VoidPaymentCommand>
{
    public async Task Handle(VoidPaymentCommand request, CancellationToken cancellationToken)
    {
        var policy = await settings.GetStringAsync(PaymentsSettings.ModificationPolicyKey, cancellationToken)
                     ?? PaymentsSettings.PolicyFull;
        if (policy != PaymentsSettings.PolicyFull)
            throw new InvalidOperationException("Voiding payments is disabled by the payment policy.");

        var reason = request.Data.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A void reason is required.");

        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {request.Id} not found");

        // Reverse this payment's applications so each invoice's computed balance reopens.
        var affectedInvoiceIds = payment.Applications.Select(a => a.InvoiceId).Distinct().ToList();
        foreach (var app in payment.Applications.ToList())
            db.Remove(app);

        payment.DeletedAt = DateTimeOffset.UtcNow;

        db.LogActivityAt("voided", $"Payment {payment.PaymentNumber} voided: {reason}",
            ("Payment", payment.Id), ("Customer", payment.CustomerId));

        await db.SaveChangesAsync(cancellationToken);

        // Recompute each affected invoice's status from its remaining applications.
        foreach (var invoiceId in affectedInvoiceIds)
        {
            var invoice = await db.Invoices
                .Include(i => i.PaymentApplications)
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice is null) continue;

            invoice.Status = invoice.AmountPaid <= 0
                ? InvoiceStatus.Sent
                : invoice.AmountPaid >= invoice.Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }

        if (affectedInvoiceIds.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
