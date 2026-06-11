using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorPayments;

/// <summary>
/// Voids (reverses) a vendor payment — the AP counterpart of the AR <c>VoidPayment</c>, WITH the GL
/// reversal the books require: drops the payment's applications so the bills reopen (status recomputed
/// from the restored balance), reverses the cash-disbursement origination journal (incl. any realized-FX
/// plug; no-op while CAP-ACCT-FULLGL is off), cancels any non-terminal bank transmission, and soft-deletes
/// the payment (lossless — the row + activity trail are preserved; <c>SetTimestamps</c> stamps DeletedBy).
/// <para>
/// HARD STOP: once the latest transmission has <b>Succeeded</b>, money has been transmitted — the void is
/// rejected; corrections must be made with a new transaction. (This also means a settlement entry can
/// never exist at void time, so reversing the origination alone restores the GL exactly.)
/// </para>
/// </summary>
public record VoidVendorPaymentCommand(int Id, string Reason) : IRequest;

public class VoidVendorPaymentHandler(
    IVendorPaymentRepository repo,
    AppDbContext db,
    IClock clock,
    // Optional / null-default (mirrors CreateVendorPayment): production DI supplies both; with
    // CAP-ACCT-FULLGL off the posting service no-ops anyway.
    IVendorPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null)
    : IRequestHandler<VoidVendorPaymentCommand>
{
    public async Task Handle(VoidVendorPaymentCommand request, CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A void reason is required.");

        var payment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Vendor payment {request.Id} not found");

        // Once the bank accepted the submission the cash is (or will be) gone — voiding the record would
        // desync it from reality. Failed/Queued/Retrying transmissions have moved no money yet.
        var latestTransmission = await repo.FindLatestTransmissionAsync(payment.Id, cancellationToken);
        if (latestTransmission?.Status == PaymentTransmissionStatus.Succeeded)
            throw new InvalidOperationException(
                $"Cannot void vendor payment {payment.PaymentNumber}: money has been transmitted; "
                + "corrections must be made with a new transaction.");

        // One unit of work (mirrors CreateVendorPayment): transmission cancellation + GL reversal +
        // application removal + bill-status restore + soft delete commit (or roll back) together.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Cancel every non-terminal transmission so no queued/retrying/re-queueable submission can move
        // money for a voided payment. (FindLatestTransmissionAsync is no-tracking — reload tracked.)
        var openTransmissions = await db.PaymentTransmissions
            .Where(t => t.SourceType == "VendorPayment" && t.SourceId == payment.Id
                && (t.Status == PaymentTransmissionStatus.Queued
                    || t.Status == PaymentTransmissionStatus.Retrying
                    || t.Status == PaymentTransmissionStatus.Failed))
            .ToListAsync(cancellationToken);
        foreach (var transmission in openTransmissions)
        {
            transmission.Status = PaymentTransmissionStatus.Cancelled;
            transmission.NextAttemptAt = null;
        }

        // GL reversal of the origination entry (Dr AP / Cr CASH-or-CIT + FX plug), FULLGL-respecting:
        // the posting service self-gates and no-ops when nothing was posted.
        if (cashPosting is not null)
        {
            var userId = int.TryParse(
                httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var uid) ? uid : 0;
            await cashPosting.ReverseVendorPaymentCreatedAsync(payment.Id, reason, userId, cancellationToken);
        }

        // Drop the applications so each bill's computed balance reopens.
        var affectedBillIds = payment.Applications.Select(a => a.VendorBillId).Distinct().ToList();
        foreach (var app in payment.Applications.ToList())
            db.Remove(app);

        // Soft delete — SetTimestamps stamps DeletedBy from the current principal automatically.
        payment.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "voided",
            $"Vendor payment {payment.PaymentNumber} voided: {reason}",
            ("VendorPayment", payment.Id));

        await db.SaveChangesAsync(cancellationToken);

        // Recompute each affected bill's status from its restored balance (mirror of the bill-status
        // logic in CreateVendorPayment): Paid/PartiallyPaid → PartiallyPaid if anything else is still
        // applied, else back to Approved (payable again).
        foreach (var billId in affectedBillIds)
        {
            var bill = await db.VendorBills
                .Include(b => b.Lines)
                .Include(b => b.PaymentApplications)
                .FirstOrDefaultAsync(b => b.Id == billId, cancellationToken);
            if (bill is null) continue;

            if (bill.Status is VendorBillStatus.Paid or VendorBillStatus.PartiallyPaid)
                bill.Status = bill.AmountPaid > 0
                    ? VendorBillStatus.PartiallyPaid
                    : VendorBillStatus.Approved;

            db.LogActivityAt(
                "payment-voided",
                $"Payment {payment.PaymentNumber} voided — balance {bill.BalanceDue:C} restored",
                ("VendorBill", bill.Id));
        }

        if (affectedBillIds.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}
