using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Banking;

/// <summary>
/// ⚡ BANKING BOUNDARY — applies a bank's ACH return/NOC file (BANK-002 Phase C). The stored
/// per-entry trace numbers are the join key:
/// <list type="bullet">
///   <item><b>Payment return (R-code):</b> the payment's transmission flips to Failed with the
///         reason — the existing Payables triage (banner, retry/void flow) takes over; the
///         payment creator gets a critical notification. The money correction (the credit came
///         back) is the controller's call: void the payment (reopens the bill, reverses the GL)
///         or re-send to a corrected account.</item>
///   <item><b>Prenote return:</b> the bank rejected the account — it flips to Disabled (numbers
///         must be re-entered, which re-runs dual control + prenote).</item>
///   <item><b>NOC (C-code):</b> a correction notice, not a failure — recorded on the vendor's
///         activity trail with the bank's corrected data; payments keep flowing.</item>
/// </list>
/// Idempotent per entry: an already-failed transmission / already-disabled account is skipped,
/// so re-importing the same file (or the SFTP poll re-reading one) double-applies nothing.
/// </summary>
public interface IBankReturnsService
{
    Task<BankReturnsImportResultModel> ApplyAsync(string fileContents, int? actorUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class BankReturnsService(
    AppDbContext db,
    IMediator mediator) : IBankReturnsService
{
    public async Task<BankReturnsImportResultModel> ApplyAsync(
        string fileContents, int? actorUserId, CancellationToken ct = default)
    {
        var entries = NachaReturnParser.Parse(fileContents);
        if (entries.Count == 0)
            throw new InvalidOperationException(
                "No return or NOC entries found — is this a NACHA return file?");

        int paymentsReturned = 0, prenotesRejected = 0, nocs = 0, unmatched = 0, skipped = 0;

        foreach (var entry in entries)
        {
            // Traces are globally unique from this build forward; OrderByDescending guards
            // legacy duplicates (pre-fix data) by preferring the most recent assignment.
            var item = await db.PaymentBatchItems
                .Include(i => i.VendorPayment)
                .Include(i => i.VendorBankAccount).ThenInclude(a => a.Vendor)
                .Where(i => i.TraceNumber == entry.OriginalTraceNumber)
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);

            if (item is null)
            {
                unmatched++;
                Log.Warning("Bank return references unknown trace {Trace} ({Code}) — skipped.",
                    entry.OriginalTraceNumber, entry.ReasonCode);
                continue;
            }

            var reason = $"{entry.ReasonCode} — {NachaReturnParser.Describe(entry.ReasonCode)}";

            if (entry.IsNotificationOfChange)
            {
                nocs++;
                db.LogActivityAt(
                    "bank-noc",
                    $"Bank NOC {reason} for account '{item.VendorBankAccount.Nickname}' "
                    + $"({item.VendorBankAccount.AccountNumberMasked}); corrected data: {entry.CorrectedData}. "
                    + "Update the account numbers when convenient (re-runs dual control + prenote).",
                    ("Vendor", item.VendorBankAccount.VendorId));
                continue;
            }

            if (item.VendorPaymentId is null)
            {
                // Prenote rejected → the account is not usable; numbers must be re-entered.
                if (item.VendorBankAccount.Status == VendorBankAccountStatus.Disabled)
                {
                    skipped++;
                    continue;
                }
                prenotesRejected++;
                item.VendorBankAccount.Status = VendorBankAccountStatus.Disabled;
                db.LogActivityAt(
                    "bank-account-prenote-returned",
                    $"Prenote RETURNED ({reason}) for account '{item.VendorBankAccount.Nickname}' "
                    + $"({item.VendorBankAccount.AccountNumberMasked}) — account disabled; re-enter the numbers.",
                    ("Vendor", item.VendorBankAccount.VendorId));
                continue;
            }

            // Payment return: flip the latest transmission to Failed (idempotent) → Payables triage.
            var transmission = await db.PaymentTransmissions
                .Where(t => t.SourceType == "VendorPayment" && t.SourceId == item.VendorPaymentId.Value)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync(ct);

            if (transmission is null || transmission.Status == PaymentTransmissionStatus.Failed)
            {
                skipped++;
                continue;
            }

            paymentsReturned++;
            transmission.Status = PaymentTransmissionStatus.Failed;
            transmission.LastError = $"ACH RETURNED by the bank: {reason} (trace {entry.OriginalTraceNumber})";

            db.LogActivityAt(
                "transmission-returned",
                $"Payment {item.VendorPayment!.PaymentNumber} RETURNED by the bank: {reason}",
                ("VendorPayment", item.VendorPaymentId.Value));

            // Best-effort critical notification to whoever released/created the transmission.
            try
            {
                var notifyUserId = transmission.CreatedByUserId ?? actorUserId;
                if (notifyUserId is int userId && userId > 0)
                {
                    await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                        UserId: userId,
                        Type: "alert",
                        Severity: "critical",
                        Source: "ach-return",
                        Title: "ACH payment returned",
                        Message: $"Payment {item.VendorPayment.PaymentNumber} ({entry.Amount:C}) was returned by the "
                                 + $"bank: {reason}. Void it (reopens the bill) or fix the vendor's bank account and re-send.",
                        EntityType: "VendorPayment",
                        EntityId: item.VendorPaymentId.Value,
                        SenderId: null)), ct);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ACH-return notification failed for payment {PaymentId}; return is applied.",
                    item.VendorPaymentId);
            }
        }

        await db.SaveChangesAsync(ct);

        Log.Information(
            "Bank returns applied: {Returned} payment return(s), {Prenotes} prenote rejection(s), {Nocs} NOC(s), "
            + "{Unmatched} unmatched, {Skipped} already-applied.",
            paymentsReturned, prenotesRejected, nocs, unmatched, skipped);

        return new BankReturnsImportResultModel(
            entries.Count, paymentsReturned, prenotesRejected, nocs, unmatched, skipped);
    }
}
