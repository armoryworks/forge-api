using Microsoft.EntityFrameworkCore;

using QBEngineer.Core.Enums;
using QBEngineer.Core.Interfaces;
using QBEngineer.Data.Context;

namespace QBEngineer.Api.Jobs;

/// <summary>
/// Phase 1r / Batch 9 — nightly LeadSource.QualityScore recompute.
///
/// Per-source quality score is a rolling 0-100 driven by disposition
/// outcomes on the source's leads:
///   start at 50, +5 per Converted lead, +2 per Engaged lead,
///   -5 per BadData / Suppressed lead, -2 per Lost lead.
/// Score is clamped to [0, 100]. Only leads created in the last 180 days
/// count toward the score — the window is what makes the score "rolling".
///
/// LastScoredAt timestamps every recompute so admin UI can show when the
/// score is stale (a backlog of unscored leads accumulated faster than
/// the nightly job).
/// </summary>
public class RecomputeLeadSourceQualityJob(
    AppDbContext db,
    IClock clock,
    ILogger<RecomputeLeadSourceQualityJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var windowStart = now.AddDays(-180);

        var sources = await db.LeadSources.Where(s => s.IsActive).ToListAsync(ct);
        if (sources.Count == 0)
        {
            logger.LogInformation("LeadSource quality: no active sources; skipping.");
            return;
        }

        // Pull the in-window leads grouped by source for a single round
        // trip — avoids N+1.
        var counts = await db.Leads.AsNoTracking()
            .Where(l => l.LeadSourceId != null && l.CreatedAt >= windowStart)
            .GroupBy(l => new { SourceId = l.LeadSourceId!.Value, l.Status, l.OutreachState })
            .Select(g => new { g.Key.SourceId, g.Key.Status, g.Key.OutreachState, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var source in sources)
        {
            var rows = counts.Where(c => c.SourceId == source.Id).ToList();
            var converted = rows.Where(r => r.Status == LeadStatus.Converted).Sum(r => r.Count);
            var lost = rows.Where(r => r.Status == LeadStatus.Lost).Sum(r => r.Count);
            var badData = rows.Where(r => r.OutreachState == OutreachState.BadData).Sum(r => r.Count);
            var suppressed = rows.Where(r => r.OutreachState == OutreachState.Suppressed).Sum(r => r.Count);
            var engaged = rows.Where(r => r.OutreachState == OutreachState.Engaged).Sum(r => r.Count);

            var score = 50
                + 5 * converted
                + 2 * engaged
                - 5 * (badData + suppressed)
                - 2 * lost;

            source.QualityScore = Math.Clamp(score, 0, 100);
            source.LastScoredAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("LeadSource quality: recomputed {Count} sources.", sources.Count);
    }
}
