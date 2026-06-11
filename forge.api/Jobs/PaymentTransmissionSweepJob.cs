using Hangfire;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Self-healing sweep for stuck <c>PaymentTransmission</c> rows (§7 BANK-002 pipeline hygiene). The
/// happy path drives itself — CreateVendorPayment enqueues, failures schedule their own backoff — but a
/// Hangfire hiccup (lost enqueue, server restart between the DB write and the schedule) can strand a row
/// as Queued-never-attempted or Retrying-past-its-slot forever, leaving real money in limbo with nothing
/// watching. Every run finds up to <see cref="MaxPerSweep"/> transmissions that are:
/// <list type="bullet">
///   <item><b>Queued</b> with no attempt yet, created more than <see cref="StuckThreshold"/> ago; or</item>
///   <item><b>Retrying</b> whose <c>NextAttemptAt</c> passed more than <see cref="StuckThreshold"/> ago,</item>
/// </list>
/// logs a warning, and re-enqueues <see cref="PaymentTransmissionJob.ProcessAsync"/> for each. The job's
/// own status checks make a double-enqueue harmless (a non-Queued/Retrying row is a no-op), and the GL
/// settlement posting is engine-idempotent.
/// </summary>
public class PaymentTransmissionSweepJob(
    AppDbContext db,
    IBackgroundJobClient backgroundJobs,
    IClock clock,
    ILogger<PaymentTransmissionSweepJob> logger)
{
    /// <summary>Cap per run — a pathological backlog drains over successive sweeps, not in one bite.</summary>
    public const int MaxPerSweep = 50;

    /// <summary>Grace period before a row counts as stuck (the longest scheduled backoff slot is 64 min,
    /// but each slot self-schedules — only a LOST schedule leaves NextAttemptAt in the past).</summary>
    public static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(10);

    public async Task SweepAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow - StuckThreshold;

        var stuck = await db.PaymentTransmissions.AsNoTracking()
            .Where(t =>
                (t.Status == PaymentTransmissionStatus.Queued
                    && t.AttemptCount == 0
                    && t.CreatedAt < cutoff)
                || (t.Status == PaymentTransmissionStatus.Retrying
                    && t.NextAttemptAt != null
                    && t.NextAttemptAt < cutoff))
            .OrderBy(t => t.Id)
            .Take(MaxPerSweep)
            .ToListAsync(ct);

        foreach (var transmission in stuck)
        {
            logger.LogWarning(
                "PaymentTransmissionSweepJob: transmission {Id} ({SourceType} {SourceId}) is stuck "
                + "({Status}, attempts {Attempts}, next attempt {NextAttemptAt}) — re-enqueueing",
                transmission.Id, transmission.SourceType, transmission.SourceId,
                transmission.Status, transmission.AttemptCount, transmission.NextAttemptAt);

            backgroundJobs.Enqueue<PaymentTransmissionJob>(
                j => j.ProcessAsync(transmission.Id, CancellationToken.None));
        }

        if (stuck.Count > 0)
            logger.LogInformation(
                "PaymentTransmissionSweepJob: re-enqueued {Count} stuck transmission(s)", stuck.Count);
    }
}
