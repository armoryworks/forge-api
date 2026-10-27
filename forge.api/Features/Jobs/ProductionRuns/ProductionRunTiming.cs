using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

/// <summary>
/// Derives a completed run's run time when nobody entered one. With both timestamps it is
/// CompletedAt − StartedAt, less the setup time and less the downtime logged in that window against the run's
/// work center or its tooling (overlapping downtime counted once, open downtime counted up to the run's end).
/// Without both timestamps it falls back to the run-type labour clocked on the job at the run's work center
/// (by the time entry's work center, or its operation's), less the run time already credited to the job's other
/// completed runs at that work center so labour is never counted twice. Null when neither source has time.
/// </summary>
public static class ProductionRunTiming
{
    public static async Task<decimal?> DeriveRunTimeMinutesAsync(AppDbContext db, ProductionRun run, CancellationToken ct)
    {
        if (run.StartedAt is DateTimeOffset start && run.CompletedAt is DateTimeOffset end)
        {
            if (end <= start)
                return null;

            var downtime = await DowntimeMinutesAsync(db, run, start, end, ct);
            var elapsed = (decimal)(end - start).TotalMinutes - (run.SetupTimeMinutes ?? 0m) - downtime;
            return Math.Round(Math.Max(0m, elapsed), 2, MidpointRounding.AwayFromZero);
        }

        return await LabourRunMinutesAsync(db, run, ct);
    }

    public static async Task<decimal> DowntimeMinutesAsync(
        AppDbContext db, ProductionRun run, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var workCenterId = run.WorkCenterId;
        var toolId = run.ToolingAssetId;
        if (workCenterId is null && toolId is null)
            return 0m;

        var logs = await db.DowntimeLogs.AsNoTracking()
            .Where(d => d.StartedAt < end
                && (d.EndedAt == null || d.EndedAt > start)
                && ((workCenterId != null && d.WorkCenterId == workCenterId) || (toolId != null && d.AssetId == toolId)))
            .Select(d => new { d.StartedAt, d.EndedAt })
            .ToListAsync(ct);

        var intervals = logs
            .Select(d => (From: d.StartedAt < start ? start : d.StartedAt, To: d.EndedAt is DateTimeOffset e && e < end ? e : end))
            .Where(i => i.To > i.From)
            .OrderBy(i => i.From)
            .ToList();

        var total = 0m;
        DateTimeOffset? currentFrom = null;
        DateTimeOffset currentTo = default;
        foreach (var (from, to) in intervals)
        {
            if (currentFrom is null)
            {
                (currentFrom, currentTo) = (from, to);
            }
            else if (from <= currentTo)
            {
                if (to > currentTo) currentTo = to;
            }
            else
            {
                total += (decimal)(currentTo - currentFrom.Value).TotalMinutes;
                (currentFrom, currentTo) = (from, to);
            }
        }
        if (currentFrom is not null)
            total += (decimal)(currentTo - currentFrom.Value).TotalMinutes;

        return total;
    }

    private static async Task<decimal?> LabourRunMinutesAsync(AppDbContext db, ProductionRun run, CancellationToken ct)
    {
        if (run.WorkCenterId is not int workCenterId)
            return null;

        var labour = await db.TimeEntries.AsNoTracking()
            .Where(t => t.JobId == run.JobId
                && t.EntryType == TimeEntryType.Run
                && (t.WorkCenterId == workCenterId || (t.WorkCenterId == null && t.Operation != null && t.Operation.WorkCenterId == workCenterId)))
            .SumAsync(t => (decimal?)t.DurationMinutes, ct) ?? 0m;
        if (labour <= 0m)
            return null;

        var credited = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.JobId == run.JobId
                && r.Id != run.Id
                && r.WorkCenterId == workCenterId
                && r.Status == ProductionRunStatus.Completed
                && r.RunTimeMinutes != null)
            .SumAsync(r => r.RunTimeMinutes, ct) ?? 0m;

        var remaining = labour - credited;
        return remaining > 0m ? Math.Round(remaining, 2, MidpointRounding.AwayFromZero) : null;
    }
}
