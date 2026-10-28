using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Core.Entities;
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
/// An ACH return reuses the same unwind through <see cref="VoidVendorPaymentHandler.ReverseBankReturnAsync"/>.
/// <para>
/// HARD STOP: once the latest transmission has <b>Succeeded</b>, money has been transmitted — the void is
/// rejected; corrections must be made with a new transaction. A bank return is the exception: the money
/// came back, so the settlement entry is reversed along with the origination.
/// </para>
/// </summary>
public record VoidVendorPaymentCommand(int Id, string Reason) : IRequest;

public class VoidVendorPaymentValidator : AbstractValidator<VoidVendorPaymentCommand>
{
    public VoidVendorPaymentValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public class VoidVendorPaymentHandler(
    IVendorPaymentRepository repo,
    AppDbContext db,
    IClock clock,
    // Optional / null-default (mirrors CreateVendorPayment): production DI supplies both; with
    // CAP-ACCT-FULLGL off the posting service no-ops anyway.
    IVendorPaymentCashPostingService? cashPosting = null,
    IHttpContextAccessor? httpContextAccessor = null,
    IVendorCreditMemoPostingService? creditPosting = null)
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

        var userId = int.TryParse(
            httpContextAccessor?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var uid) ? uid : 0;
        await ReverseAsync(payment, reason, userId, bankReturn: null, cancellationToken);
    }

    /// <summary>
    /// Reverses a vendor payment the bank returned (ACH R-code) through the same unwind as a void: the bills
    /// reopen with their balances (and any discount taken) restored, the cash-disbursement and settlement
    /// journals reverse at their booked rates on the return date, open transmissions are cancelled and the
    /// payment is voided with the return code on its audit trail. The transmitted-money hard stop does not
    /// apply: the money came back. A return dated in a closed period reverses on the first day of the first Open
    /// period after it; the trail keeps the actual return date. Returns the date the journals reversed on, or
    /// null when nothing was posted to reverse (or the payment is already voided, which changes nothing).
    /// </summary>
    public async Task<DateOnly?> ReverseBankReturnAsync(
        int vendorPaymentId, VendorPaymentBankReturn bankReturn, int userId, CancellationToken cancellationToken)
    {
        var payment = await repo.FindWithDetailsAsync(vendorPaymentId, cancellationToken);
        if (payment is null)
            return null;

        return await ReverseAsync(payment, $"ACH return {bankReturn.Reason}", userId, bankReturn, cancellationToken);
    }

    private async Task<DateOnly?> ReverseAsync(
        VendorPayment payment, string reason, int userId, VendorPaymentBankReturn? bankReturn,
        CancellationToken cancellationToken)
    {
        // One unit of work (mirrors CreateVendorPayment): transmission cancellation + GL reversal +
        // application removal + bill-status restore + soft delete commit (or roll back) together.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Cancel every non-terminal transmission so no queued/retrying/re-queueable submission can move
        // money for a voided payment. (FindLatestTransmissionAsync is no-tracking — reload tracked.)
        var openTransmissions = await db.PaymentTransmissions
            .Where(t => t.SourceType == "VendorPayment" && t.SourceId == payment.Id
                && (t.Status == PaymentTransmissionStatus.Queued
                    || t.Status == PaymentTransmissionStatus.AwaitingAttestation
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
        var reversedOn = cashPosting is null
            ? null
            : await cashPosting.ReverseVendorPaymentCreatedAsync(
                payment.Id, reason, userId, bankReturn?.ReturnDate, cancellationToken);

        var deductedCredits = await db.VendorCreditMemoApplications
            .Where(a => a.VendorPaymentId == payment.Id && a.ReversedAt == null)
            .Select(a => new { a.Id, a.VendorCreditMemoId })
            .ToListAsync(cancellationToken);
        foreach (var credit in deductedCredits)
        {
            var memo = await VendorCreditMemoRules.LoadMemoAsync(db, credit.VendorCreditMemoId, cancellationToken);
            var application = memo?.Applications.FirstOrDefault(a => a.Id == credit.Id);
            if (memo is not null && application is not null && creditPosting is not null)
                await VendorCreditMemoApplicationReversal.ReverseAsync(db, creditPosting, memo, application, clock.UtcNow, cancellationToken);
        }

        // Drop the applications so each bill's computed balance reopens.
        var affectedBillIds = payment.Applications.Select(a => a.VendorBillId).Distinct().ToList();
        var paymentMoney = await MoneyFormat.ForVendorPaymentAsync(db, payment.Id, cancellationToken);
        var appliedTo = string.Join(", ", payment.Applications
            .OrderBy(a => a.Id)
            .Select(a => $"{a.VendorBill?.BillNumber ?? $"Bill {a.VendorBillId}"} ({paymentMoney.Format(a.Amount)})"));
        foreach (var app in payment.Applications.ToList())
            db.Remove(app);

        // Soft delete — SetTimestamps stamps DeletedBy from the current principal automatically.
        payment.DeletedAt = clock.UtcNow;

        var voidedText = bankReturn is null
            ? $"Vendor payment {payment.PaymentNumber} voided: {reason}"
            : $"Vendor payment {payment.PaymentNumber} returned by the bank ({bankReturn.Reason}, trace "
              + $"{bankReturn.TraceNumber}, returned {bankReturn.ReturnDate:yyyy-MM-dd}) and reversed"
              + (reversedOn is DateOnly postedOn && postedOn != bankReturn.ReturnDate
                  ? $" on {postedOn:yyyy-MM-dd}, the first open period, because the return date's period is closed"
                  : string.Empty);
        db.LogActivityAt(
            "voided",
            appliedTo.Length == 0 ? voidedText : $"{voidedText}. It was applied to {appliedTo}.",
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
                .Include(b => b.CreditApplications)
                .FirstOrDefaultAsync(b => b.Id == billId, cancellationToken);
            if (bill is null) continue;

            if (bill.Status is VendorBillStatus.Paid or VendorBillStatus.PartiallyPaid)
                VendorCreditMemoRules.PromoteBill(bill);

            var money = await MoneyFormat.ForCurrencyIdAsync(db, bill.CurrencyId, cancellationToken);
            db.LogActivityAt(
                "payment-voided",
                bankReturn is null
                    ? $"Payment {payment.PaymentNumber} voided — balance {money.Format(bill.BalanceDue)} restored"
                    : $"Payment {payment.PaymentNumber} returned by the bank ({bankReturn.Reason}) — balance "
                      + $"{money.Format(bill.BalanceDue)} restored",
                ("VendorBill", bill.Id));
        }

        if (affectedBillIds.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return reversedOn;
    }
}
