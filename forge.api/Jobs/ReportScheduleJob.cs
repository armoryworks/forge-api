using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Reports;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Recurring Hangfire job — e-mails every active report schedule whose next run is due, then moves
/// its next run to the following cron occurrence in the shop's time zone.
/// </summary>
public class ReportScheduleJob(
    AppDbContext db,
    ReportScheduleSender sender,
    IClock clock,
    ILogger<ReportScheduleJob> logger,
    ICapabilitySnapshotProvider capabilities)
{
    internal const string CapabilityCode = "CAP-RPT-DASHBOARDS";

    public async Task RunDueAsync(CancellationToken ct)
    {
        if (!capabilities.IsEnabled(CapabilityCode))
            return;

        var now = clock.UtcNow;
        var schedules = await db.ReportSchedules
            .Include(s => s.SavedReport)
            .Where(s => s.IsActive && (s.NextRunAt == null || s.NextRunAt <= now))
            .OrderBy(s => s.NextRunAt)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var schedule in schedules)
        {
            ct.ThrowIfCancellationRequested();

            if (schedule.NextRunAt is { } dueAt)
            {
                try
                {
                    await sender.SendAsync(schedule, dueAt.UtcTicks.ToString(CultureInfo.InvariantCulture), ct);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "[ReportSchedule] Schedule {ScheduleId} failed to send", schedule.Id);
                }
            }

            schedule.NextRunAt = await sender.NextRunAfterAsync(schedule.CronExpression, now, ct);
            await db.SaveChangesAsync(ct);
        }

        if (schedules.Count > 0)
            logger.LogInformation("[ReportSchedule] Sent {Sent} of {Due} due schedule(s)", sent, schedules.Count);
    }
}
