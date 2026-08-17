using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MediatR;

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

        var overdueSchedules = await db.MaintenanceSchedules
            .Include(s => s.Asset)
            .Where(s => s.NextDueAt < now && s.IsActive && s.DeletedAt == null)
            .ToListAsync(ct);

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
            // Check if we already notified for this overdue period
            var alreadyNotified = await db.Notifications
                .AnyAsync(n => n.EntityType == "Asset"
                    && n.EntityId == schedule.AssetId
                    && n.Source == "maintenance-overdue"
                    && n.CreatedAt > schedule.NextDueAt, ct);

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
                        Title: $"Overdue Maintenance: {schedule.Title}",
                        Message: $"Maintenance on {schedule.Asset?.Name ?? "Asset"} is overdue. Scheduled for {schedule.NextDueAt:MMM dd, yyyy}.",
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
}
