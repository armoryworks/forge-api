using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Jobs;

/// <summary>
/// Processes one <c>PaymentTransmission</c>: submits the payment to the bank via
/// <see cref="IBankPaymentService"/>, retrying transient failures with exponential backoff
/// (1/4/16/64 min — ×4 per attempt). After the final attempt fails, the transmission is marked
/// Failed and the payment creator receives a critical notification so the row can be manually
/// re-queued via <c>POST /payment-transmissions/{id}/retry</c>.
/// <para>
/// On SUCCESS it additionally posts the §7 BANK-002 <b>settlement</b> entry
/// (Dr <c>CASH_IN_TRANSIT</c> / Cr <c>CASH</c>) clearing the in-transit balance the payment's
/// origination credited — skipped silently when no origination journal exists (CAP-ACCT-FULLGL was
/// off at creation) or the origination predates the cash-in-transit clearing. Idempotent via the
/// engine's (BookId, IdempotencyKey) de-dupe; a settlement-posting failure is logged but never fails
/// the transmission (the submission itself DID succeed — the lingering CIT balance is the visible
/// reconciling item for BANK-001).
/// </para>
/// </summary>
public class PaymentTransmissionJob(
    AppDbContext db,
    IBankPaymentService bankPaymentService,
    IBackgroundJobClient backgroundJobs,
    IMediator mediator,
    IClock clock,
    ILogger<PaymentTransmissionJob> logger,
    // Optional / null-default (mirrors the posting seams elsewhere): production DI supplies the engine;
    // unit tests without GL seed skip the settlement posting entirely when null.
    IPostingEngine? postingEngine = null)
{
    /// <summary>One initial attempt + four automatic retries (spec: retry up to 4 more times).</summary>
    public const int MaxAttempts = 5;

    /// <summary>Backoff before retry N (index = attemptCount - 1): exponential ×4.</summary>
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(16),
        TimeSpan.FromMinutes(64),
    ];

    /// <summary>Delay scheduled after failed attempt <paramref name="attemptCount"/> (1-based).</summary>
    public static TimeSpan DelayForAttempt(int attemptCount)
        => RetryDelays[Math.Clamp(attemptCount - 1, 0, RetryDelays.Length - 1)];

    public async Task ProcessAsync(int transmissionId, CancellationToken ct)
    {
        var transmission = await db.PaymentTransmissions
            .FirstOrDefaultAsync(t => t.Id == transmissionId, ct);

        if (transmission is null)
        {
            logger.LogWarning("PaymentTransmissionJob: transmission {Id} not found — skipping", transmissionId);
            return;
        }

        if (transmission.Status is not (PaymentTransmissionStatus.Queued or PaymentTransmissionStatus.Retrying))
        {
            logger.LogInformation(
                "PaymentTransmissionJob: transmission {Id} is {Status} — nothing to do",
                transmissionId, transmission.Status);
            return;
        }

        transmission.AttemptCount++;
        transmission.LastAttemptAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        // Build the channel request; for VendorPayment sources enrich with reference + vendor name.
        string? referenceNumber = null;
        string? vendorName = null;
        var paymentNumber = $"{transmission.SourceType} {transmission.SourceId}";
        if (transmission.SourceType == "VendorPayment")
        {
            var payment = await db.VendorPayments.AsNoTracking()
                .Include(p => p.Vendor)
                .FirstOrDefaultAsync(p => p.Id == transmission.SourceId, ct);
            if (payment is not null)
            {
                referenceNumber = payment.ReferenceNumber;
                vendorName = payment.Vendor?.CompanyName;
                paymentNumber = payment.PaymentNumber;
            }
        }

        var request = new BankPaymentRequest(
            transmission.SourceType, transmission.SourceId,
            transmission.Amount, transmission.Method, referenceNumber, vendorName);

        BankSubmissionResult result;
        try
        {
            result = await bankPaymentService.SubmitPaymentAsync(request, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A thrown exception from the channel counts as a failed attempt.
            result = new BankSubmissionResult(false, null, ex.Message);
        }

        if (result.Success)
        {
            transmission.Status = PaymentTransmissionStatus.Succeeded;
            transmission.SubmissionRef = result.SubmissionRef;
            transmission.NextAttemptAt = null;
            transmission.LastError = null;
            db.LogActivityAt(
                "transmission-succeeded",
                $"Bank transmission succeeded — ref {result.SubmissionRef}",
                (transmission.SourceType, transmission.SourceId));
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "PaymentTransmissionJob: transmission {Id} succeeded on attempt {Attempt} (ref {Ref})",
                transmissionId, transmission.AttemptCount, result.SubmissionRef);

            // §7 BANK-002: the bank accepted the submission → clear the cash-in-transit balance the
            // origination credited. Best-effort: a posting failure must NOT fail the transmission.
            await TryPostSettlementAsync(transmission, paymentNumber, ct);
            return;
        }

        transmission.LastError = result.Error;

        if (transmission.AttemptCount < MaxAttempts)
        {
            var delay = DelayForAttempt(transmission.AttemptCount);
            transmission.Status = PaymentTransmissionStatus.Retrying;
            transmission.NextAttemptAt = clock.UtcNow.Add(delay);
            await db.SaveChangesAsync(ct);

            backgroundJobs.Schedule<PaymentTransmissionJob>(
                j => j.ProcessAsync(transmissionId, CancellationToken.None), delay);

            logger.LogWarning(
                "PaymentTransmissionJob: transmission {Id} attempt {Attempt}/{Max} failed ({Error}) — retrying in {Delay}",
                transmissionId, transmission.AttemptCount, MaxAttempts, result.Error, delay);
            return;
        }

        // Final attempt failed → terminal Failed + critical notification for manual triage.
        transmission.Status = PaymentTransmissionStatus.Failed;
        transmission.NextAttemptAt = null;
        db.LogActivityAt(
            "transmission-failed",
            $"Bank transmission failed after {transmission.AttemptCount} attempts — {Truncate(result.Error, 100)}",
            (transmission.SourceType, transmission.SourceId));
        await db.SaveChangesAsync(ct);

        logger.LogError(
            "PaymentTransmissionJob: transmission {Id} FAILED after {Attempts} attempts: {Error}",
            transmissionId, transmission.AttemptCount, result.Error);

        // Notification dispatch is best-effort — its failure must not fail (or re-run) the job.
        try
        {
            if (transmission.CreatedByUserId is int userId && userId > 0)
            {
                await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                    UserId: userId,
                    Type: "alert",
                    Severity: "critical",
                    Source: "payment-transmission-failed",
                    Title: "Payment transmission failed",
                    Message: $"Payment {paymentNumber} ({transmission.Amount:C}) could not be submitted to the bank "
                             + $"after {transmission.AttemptCount} attempts: {Truncate(transmission.LastError, 500)}. "
                             + "Reprocess it from the Payables screen.",
                    EntityType: transmission.SourceType,
                    EntityId: transmission.SourceId,
                    SenderId: null)), ct);
            }
            else
            {
                logger.LogWarning(
                    "PaymentTransmissionJob: transmission {Id} has no creator user — failure notification skipped",
                    transmissionId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PaymentTransmissionJob: failed to send failure notification for transmission {Id}",
                transmissionId);
        }
    }

    /// <summary>
    /// Posts the §7 BANK-002 settlement entry (Dr CASH_IN_TRANSIT / Cr CASH) for a successfully
    /// submitted vendor payment, gated + idempotent:
    /// <list type="bullet">
    ///   <item>Skips silently when no origination journal exists for the payment (CAP-ACCT-FULLGL was
    ///         off at creation) or the origination carries no Cr CASH_IN_TRANSIT (legacy pre-CIT entry).</item>
    ///   <item>Settles the origination's exact in-transit FUNCTIONAL amount (which differs from the
    ///         payment amount on realized-FX settlements).</item>
    ///   <item>Re-runs are safe via the engine's (BookId, IdempotencyKey) de-dupe.</item>
    ///   <item>NEVER fails the transmission — the bank submission itself succeeded; a posting failure is
    ///         logged and the lingering CIT balance becomes the visible BANK-001 reconciling item.</item>
    /// </list>
    /// </summary>
    private async Task TryPostSettlementAsync(
        PaymentTransmission transmission, string paymentNumber, CancellationToken ct)
    {
        if (postingEngine is null || transmission.SourceType != "VendorPayment")
            return;

        try
        {
            // Resolve the active posting book — mirrors how the posting services load it.
            var book = await db.Books.AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(ct);
            if (book is null)
                return; // No GL configured — nothing was originated, nothing to settle.

            // Locate the origination entry by its idempotency key (the posting service's key shape).
            var originationKey = $"{JournalSource.AP}:VendorPayment:{transmission.SourceId}:PAYMENT";
            var origination = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
                .Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.BookId == book.Id && e.IdempotencyKey == originationKey, ct);
            if (origination is null)
                return; // FULLGL was off when the payment was created — skip silently.

            // Sum the origination's Cr CASH_IN_TRANSIT functional amount. Zero → legacy/non-CIT entry.
            var citAccountId = await db.AccountDeterminationRules.AsNoTracking()
                .Where(r => r.BookId == book.Id && r.Key == "CASH_IN_TRANSIT")
                .Select(r => (int?)r.GlAccountId)
                .FirstOrDefaultAsync(ct);
            if (citAccountId is null)
                return; // CIT account not seeded on this install — pre-CIT behavior, nothing in transit.

            var inTransit = origination.Lines
                .Where(l => l.GlAccountId == citAccountId && l.Credit > 0m)
                .Sum(l => l.FunctionalAmount);
            if (inTransit <= 0m)
                return; // Origination credited CASH directly (legacy) — nothing in transit to clear.

            var request = new PostingRequest
            {
                BookId = book.Id,
                EntryDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                Source = JournalSource.AP,
                SourceType = "VendorPayment",
                SourceId = transmission.SourceId,
                CurrencyId = book.FunctionalCurrencyId,
                Memo = $"Bank settlement — payment {paymentNumber}",
                IdempotencyKey = $"{JournalSource.AP}:VendorPayment:{transmission.SourceId}:SETTLEMENT",
                Lines =
                [
                    new PostingLine
                    {
                        AccountKey = "CASH_IN_TRANSIT",
                        Debit = inTransit,
                        Description = $"Bank settlement — payment {paymentNumber}",
                    },
                    new PostingLine
                    {
                        AccountKey = "CASH",
                        Credit = inTransit,
                        Description = $"Bank settlement — payment {paymentNumber}",
                    },
                ],
            };

            // Hangfire context carries no user principal; the SoD boundary fail-safe-denies without one.
            // Enter the explicit system-posting scope (§5.7 carve-out) so this trusted, idempotent
            // settlement entry is authorized — and logged — as the system principal.
            using (GlSystemPostingScope.Enter())
            {
                await postingEngine.PostAsync(request, transmission.CreatedByUserId ?? 0, ct);
            }

            logger.LogInformation(
                "PaymentTransmissionJob: settlement posted for transmission {Id} ({Amount} in transit cleared)",
                transmission.Id, inTransit);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The submission DID succeed — never unwind the Succeeded transmission over a settlement
            // posting failure. The un-cleared CASH_IN_TRANSIT balance is the visible reconciling item.
            logger.LogError(ex,
                "PaymentTransmissionJob: settlement posting failed for transmission {Id} (payment {Payment}); "
                + "the transmission remains Succeeded — the cash-in-transit balance will surface in reconciliation",
                transmission.Id, paymentNumber);
        }
    }

    private static string Truncate(string? s, int max)
        => s is null ? string.Empty : s.Length <= max ? s : s[..max];
}
