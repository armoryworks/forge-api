using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Api.Features.Assets;
using Forge.Api.Features.Notifications;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

using Forge.Api.Capabilities;

namespace Forge.Api.Jobs;

public class OverdueMaintenanceJob(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISender mediator,
    IClock clock,
    ILogger<OverdueMaintenanceJob> logger,
    ICapabilitySnapshotProvider capabilities)
{
    public async Task CheckOverdueMaintenanceAsync(CancellationToken ct = default)
    {
        // ── Capability gate (self-gating job — the VarianceWatchdogJob pattern):
        // preventive maintenance is capability-owned; when the capability is off (services /
        // construction installs) the schedule still ticks but the job is a no-op,
        // so toggling the capability takes effect without a restart.
        if (!capabilities.IsEnabled("CAP-MAINT-PM"))
            return;

        var now = clock.UtcNow;

        var activeSchedules = await db.MaintenanceSchedules
            .Include(s => s.Asset)
            .Where(s => s.IsActive && s.DeletedAt == null)
            .ToListAsync(ct);

        var lastService = await MaintenanceUsage.LastServiceReadingsAsync(
            db, activeSchedules.Select(s => s.Id).ToList(), ct);

        var overdueSchedules = activeSchedules
            .Where(s => MaintenanceUsage.IsDue(s, s.Asset, lastService.GetValueOrDefault(s.Id), now))
            .ToList();

        if (overdueSchedules.Count == 0)
        {
            logger.LogInformation("No overdue maintenance schedules found");
            return;
        }

        logger.LogInformation(
            "Found {Count} overdue maintenance schedules — checking notifications",
            overdueSchedules.Count);

        var notifiedCount = 0;

        foreach (var schedule in overdueSchedules)
        {
            var reading = lastService.GetValueOrDefault(schedule.Id);
            var dueByDate = schedule.NextDueAt < now;
            var dueByUsage = MaintenanceUsage.IsDueByHours(schedule, schedule.Asset, reading)
                || MaintenanceUsage.IsDueByShots(schedule, schedule.Asset, reading);
            var usageWindowStart = schedule.LastPerformedAt ?? schedule.CreatedAt;
            var overdueSince = dueByDate && dueByUsage
                ? (schedule.NextDueAt < usageWindowStart ? schedule.NextDueAt : usageWindowStart)
                : dueByDate ? schedule.NextDueAt : usageWindowStart;
            var title = $"Overdue Maintenance: {schedule.Title}";

            var alreadyNotified = await db.Notifications
                .AnyAsync(n => n.EntityType == "Asset"
                    && n.EntityId == schedule.AssetId
                    && n.Source == "maintenance-overdue"
                    && n.Title == title
                    && n.CreatedAt > overdueSince, ct);

            if (alreadyNotified) continue;

            // Get Admin and Manager users to notify
            var admins = await userManager.GetUsersInRoleAsync("Admin");
            var managers = await userManager.GetUsersInRoleAsync("Manager");

            var userIds = admins.Concat(managers)
                .Select(u => u.Id)
                .Distinct()
                .ToList();

            if (userIds.Count == 0)
            {
                logger.LogWarning("No Admin or Manager users found to send overdue maintenance notification");
                continue;
            }

            foreach (var userId in userIds)
            {
                try
                {
                    await mediator.Send(new CreateNotificationCommand(new CreateNotificationRequestModel(
                        UserId: userId,
                        Type: "alert",
                        Severity: "warning",
                        Source: "maintenance-overdue",
                        Title: title,
                        Message: OverdueMessage(schedule, reading, dueByDate),
                        EntityType: "Asset",
                        EntityId: schedule.AssetId,
                        SenderId: null)), ct);
                    notifiedCount++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create maintenance notification for user {UserId}, schedule {ScheduleId}",
                        userId, schedule.Id);
                }
            }
        }

        logger.LogInformation("Sent {Count} overdue maintenance notifications", notifiedCount);
    }

    private static string OverdueMessage(MaintenanceSchedule schedule, MaintenanceServiceReading? reading, bool dueByDate)
    {
        var assetName = schedule.Asset?.Name ?? "Asset";
        var reasons = new List<string>();
        if (dueByDate)
            reasons.Add($"scheduled for {schedule.NextDueAt:MMM dd, yyyy}");
        if (schedule.Asset is not null && MaintenanceUsage.IsDueByHours(schedule, schedule.Asset, reading))
            reasons.Add($"{schedule.Asset.CurrentHours - (reading?.Hours ?? 0m):0.#} hours since last service (interval {schedule.IntervalHours:0.#})");
        if (schedule.Asset is not null && MaintenanceUsage.IsDueByShots(schedule, schedule.Asset, reading))
            reasons.Add($"{schedule.Asset.CurrentShotCount - (reading?.Shots ?? 0):N0} shots since last service (interval {schedule.IntervalShots:N0})");
        return $"Maintenance on {assetName} is overdue: {string.Join("; ", reasons)}.";
    }
}
