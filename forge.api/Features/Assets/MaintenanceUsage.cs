using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Assets;

public sealed record MaintenanceServiceReading(decimal Hours, int Shots);

public static class MaintenanceUsage
{
    public static async Task<Dictionary<int, MaintenanceServiceReading>> LastServiceReadingsAsync(
        AppDbContext db, IReadOnlyCollection<int> scheduleIds, CancellationToken ct)
    {
        if (scheduleIds.Count == 0)
            return [];

        var logs = await db.MaintenanceLogs
            .AsNoTracking()
            .Where(l => scheduleIds.Contains(l.MaintenanceScheduleId)
                && (l.HoursAtService != null || l.ShotCountAtService != null))
            .Select(l => new { l.MaintenanceScheduleId, l.PerformedAt, l.Id, l.HoursAtService, l.ShotCountAtService })
            .ToListAsync(ct);

        return logs
            .GroupBy(l => l.MaintenanceScheduleId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var ordered = g.OrderByDescending(l => l.PerformedAt).ThenByDescending(l => l.Id).ToList();
                    return new MaintenanceServiceReading(
                        ordered.FirstOrDefault(l => l.HoursAtService != null)?.HoursAtService ?? 0m,
                        ordered.FirstOrDefault(l => l.ShotCountAtService != null)?.ShotCountAtService ?? 0);
                });
    }

    public static bool IsDueByHours(MaintenanceSchedule schedule, Asset asset, MaintenanceServiceReading? lastService) =>
        schedule.IntervalHours is > 0
        && asset.CurrentHours - (lastService?.Hours ?? 0m) >= schedule.IntervalHours.Value;

    public static bool IsDueByShots(MaintenanceSchedule schedule, Asset asset, MaintenanceServiceReading? lastService) =>
        schedule.IntervalShots is > 0
        && asset.CurrentShotCount - (lastService?.Shots ?? 0) >= schedule.IntervalShots.Value;

    public static bool IsDue(MaintenanceSchedule schedule, Asset asset, MaintenanceServiceReading? lastService, DateTimeOffset now) =>
        schedule.NextDueAt < now
        || IsDueByHours(schedule, asset, lastService)
        || IsDueByShots(schedule, asset, lastService);
}
