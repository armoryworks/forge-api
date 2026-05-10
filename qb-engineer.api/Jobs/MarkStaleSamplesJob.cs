using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;
using QBEngineer.Data.Extensions;

namespace QBEngineer.Api.Jobs;

/// <summary>
/// Phase 1r / Batch 16 — nightly stale-sample sweep.
///
/// Flips SampleShipment.Status from "Delivered" to "Stale" once the
/// sample has been with the prospect for >14 days without a follow-up
/// outcome (no QuotedFromSample / LostFromSample transition). Stale
/// samples surface in the /leads/samples page so reps can chase
/// outcomes or accept that the prospect ghosted.
///
/// 14 days is a deliberate compromise between "give the prospect
/// time to evaluate" and "don't let samples disappear from the rep's
/// attention forever". Configurable via admin settings if installs
/// need to tune it.
/// </summary>
public class MarkStaleSamplesJob(
    AppDbContext db,
    IClock clock,
    ILogger<MarkStaleSamplesJob> logger)
{
    private const int StaleThresholdDays = 14;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var threshold = now.AddDays(-StaleThresholdDays);

        var candidates = await db.SampleShipments
            .Where(s => s.Status == "Delivered"
                && s.DeliveredAt != null
                && s.DeliveredAt < threshold)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            logger.LogInformation("Stale-sample sweep: no candidates older than {Days} days.", StaleThresholdDays);
            return;
        }

        foreach (var sample in candidates)
        {
            sample.Status = "Stale";
            db.LogActivityAt(
                "sample-marked-stale",
                $"Sample shipment auto-marked stale after {StaleThresholdDays} days delivered with no outcome.",
                ("Lead", sample.LeadId));
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Stale-sample sweep: marked {Count} samples stale.", candidates.Count);
    }
}
