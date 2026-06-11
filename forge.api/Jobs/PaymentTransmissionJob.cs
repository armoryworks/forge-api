using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Jobs;

/// <summary>
/// Processes one <c>PaymentTransmission</c>: submits the payment to the bank via
/// <see cref="IBankPaymentService"/>, retrying transient failures with exponential backoff
/// (1/4/16/64 min — ×4 per attempt). After the final attempt fails, the transmission is marked
/// Failed and the payment creator receives a critical notification so the row can be manually
/// re-queued via <c>POST /payment-transmissions/{id}/retry</c>.
/// </summary>
public class PaymentTransmissionJob(
    AppDbContext db,
    IBankPaymentService bankPaymentService,
    IBackgroundJobClient backgroundJobs,
    IMediator mediator,
    IClock clock,
    ILogger<PaymentTransmissionJob> logger)
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

    private static string Truncate(string? s, int max)
        => s is null ? string.Empty : s.Length <= max ? s : s[..max];
}
