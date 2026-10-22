using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Hourly Hangfire job — tells managers and production managers about timers that have been running longer than
/// <c>shop-floor.stale-timer-hours</c>, once per timer. Timers are never stopped automatically.
/// </summary>
public class StaleTimerAlertJob(
    AppDbContext db,
    ISettingsService settings,
    IClock clock,
    ILogger<StaleTimerAlertJob> logger)
{
    public const string NotificationType = "stale_timer";
    public const string EntityType = "time_entries";
    private static readonly string[] SupervisorRoles = ["Manager", "Production Manager"];

    public async Task CheckStaleTimersAsync(CancellationToken ct = default)
    {
        var configured = await settings.GetIntAsync(ShopFloorSettings.StaleTimerHoursKey, ct);
        var hours = configured > 0 ? configured : ShopFloorSettings.DefaultStaleTimerHours;
        var cutoff = clock.UtcNow.AddHours(-hours);

        var stale = await db.TimeEntries.AsNoTracking()
            .Where(t => t.TimerStart != null && t.TimerStop == null && t.TimerStart <= cutoff)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.TimerStart,
                JobNumber = t.Job != null ? t.Job.JobNumber : null,
                StepNumber = t.Operation != null ? (int?)t.Operation.StepNumber : null,
                UserName = db.Users.Where(u => u.Id == t.UserId).Select(u => u.LastName + ", " + u.FirstName).FirstOrDefault(),
            })
            .OrderBy(t => t.TimerStart)
            .Take(500)
            .ToListAsync(ct);
        if (stale.Count == 0)
            return;

        var ids = stale.Select(t => (int?)t.Id).ToList();
        var alerted = (await db.Notifications.AsNoTracking()
                .Where(n => n.Type == NotificationType && n.EntityType == EntityType && ids.Contains(n.EntityId))
                .Select(n => n.EntityId!.Value)
                .ToListAsync(ct))
            .ToHashSet();
        var fresh = stale.Where(t => !alerted.Contains(t.Id)).ToList();
        if (fresh.Count == 0)
            return;

        var supervisors = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => SupervisorRoles.Contains(x.Name!))
            .Join(db.Users.Where(u => u.IsActive), x => x.UserId, u => u.Id, (x, u) => u.Id)
            .Distinct()
            .ToListAsync(ct);
        if (supervisors.Count == 0)
        {
            logger.LogInformation("[StaleTimerAlert] {Count} stale timer(s) but no manager to tell", fresh.Count);
            return;
        }

        foreach (var timer in fresh)
        {
            var what = timer.JobNumber is null
                ? "a timer"
                : timer.StepNumber is int step ? $"a timer on {timer.JobNumber} Op {step}" : $"a timer on {timer.JobNumber}";
            var running = clock.UtcNow - timer.TimerStart!.Value;
            foreach (var supervisorId in supervisors)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = supervisorId,
                    Type = NotificationType,
                    Severity = "warning",
                    Source = "time_tracking",
                    Title = "Timer still running",
                    Message = $"{timer.UserName ?? "Someone"} has had {what} running for {(int)running.TotalHours} hours. "
                        + "Check whether it should be stopped.",
                    EntityType = EntityType,
                    EntityId = timer.Id,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("[StaleTimerAlert] Alerted {Supervisors} supervisor(s) about {Count} timer(s) running {Hours}+ hours",
            supervisors.Count, fresh.Count, hours);
    }
}
