using Microsoft.EntityFrameworkCore;

using MediatR;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record GetShopFloorOverviewQuery(int? TeamId = null) : IRequest<ShopFloorOverviewResponseModel>;

public class GetShopFloorOverviewHandler(AppDbContext db)
    : IRequestHandler<GetShopFloorOverviewQuery, ShopFloorOverviewResponseModel>
{
    public async Task<ShopFloorOverviewResponseModel> Handle(
        GetShopFloorOverviewQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        // Active jobs in shop-floor stages only (physical work, not admin/office stages)
        var activeJobs = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => !j.IsArchived && j.CompletedDate == null
                && j.CurrentStage.IsShopFloor)
            .OrderBy(j => j.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(j => j.Priority)
            .Select(j => new
            {
                j.Id,
                j.JobNumber,
                j.Title,
                StageName = j.CurrentStage.Name,
                StageColor = j.CurrentStage.Color ?? "#94a3b8",
                j.Priority,
                j.AssigneeId,
                j.DueDate,
            })
            .ToListAsync(cancellationToken);

        // Get assignee info for active jobs
        var assigneeIds = activeJobs
            .Where(j => j.AssigneeId.HasValue)
            .Select(j => j.AssigneeId!.Value)
            .Distinct()
            .ToList();

        var users = await db.Users
            .Where(u => assigneeIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.Initials,
                u.AvatarColor,
                u.FirstName,
                u.LastName,
            })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var jobModels = activeJobs.Select(j =>
        {
            var assignee = j.AssigneeId.HasValue && users.TryGetValue(j.AssigneeId.Value, out var u)
                ? u : null;
            return new ShopFloorJobResponseModel(
                j.Id,
                j.JobNumber,
                j.Title,
                j.StageName,
                j.StageColor,
                j.Priority.ToString(),
                j.AssigneeId,
                assignee?.Initials,
                assignee?.AvatarColor,
                j.DueDate?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                j.DueDate.HasValue && j.DueDate.Value.Date < today);
        }).ToList();

        // Completed today
        var completedToday = await db.Jobs
            .CountAsync(j => j.CompletedDate.HasValue && j.CompletedDate.Value.Date == today, cancellationToken);

        // Clocked-in workers: last clock event per user is ClockIn
        // Use subquery approach to avoid unsupported GroupBy + First() projection
        var todayEvents = await db.ClockEvents
            .Where(e => e.Timestamp.Date == today)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken);

        var lastClockEvents = todayEvents
            .GroupBy(e => e.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                LastEvent = g.First(),
            })
            .Where(x => x.LastEvent.EventType == ClockEventType.ClockIn)
            .ToList();

        var clockedInUserIds = lastClockEvents.Select(x => x.UserId).ToList();

        var workerModels = new List<ShopFloorWorkerResponseModel>();

        if (clockedInUserIds.Count > 0)
        {
            var clockedInUsersQuery = db.Users
                .Where(u => clockedInUserIds.Contains(u.Id));
            if (request.TeamId.HasValue)
                clockedInUsersQuery = clockedInUsersQuery.Where(u => u.TeamId == request.TeamId.Value);

            var clockedInUsers = await clockedInUsersQuery
                .Select(u => new
                {
                    u.Id,
                    Name = (u.FirstName + " " + u.LastName).Trim(),
                    u.Initials,
                    u.AvatarColor,
                })
                .ToListAsync(cancellationToken);

            // Active timers for clocked-in workers
            var activeTimers = await db.TimeEntries
                .Include(t => t.Job)
                .Where(t => clockedInUserIds.Contains(t.UserId) && t.TimerStart != null && t.TimerStop == null)
                .ToListAsync(cancellationToken);

            var timersByUser = activeTimers.ToDictionary(t => t.UserId);

            foreach (var user in clockedInUsers)
            {
                var clockInTime = lastClockEvents.First(e => e.UserId == user.Id).LastEvent.Timestamp;
                timersByUser.TryGetValue(user.Id, out var activeTimer);

                var timeOnTask = activeTimer?.TimerStart != null
                    ? FormatDuration(now - activeTimer.TimerStart.Value)
                    : FormatDuration(now - clockInTime);

                workerModels.Add(new ShopFloorWorkerResponseModel(
                    user.Id,
                    user.Name,
                    user.Initials ?? "??",
                    user.AvatarColor ?? "#94a3b8",
                    activeTimer?.Job?.Title,
                    activeTimer?.JobId,
                    activeTimer?.Job?.JobNumber,
                    timeOnTask));
            }
        }

        // Maintenance alerts: jobs in Production track with "Maintenance" in stage name or track name
        var maintenanceAlerts = await db.Jobs
            .Include(j => j.TrackType)
            .CountAsync(j => !j.IsArchived
                && j.CompletedDate == null
                && j.TrackType.Name.Contains("Maintenance")
                && j.DueDate.HasValue
                && j.DueDate.Value.Date <= today, cancellationToken);

        return new ShopFloorOverviewResponseModel(jobModels, workerModels, completedToday, maintenanceAlerts);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m {duration.Seconds:D2}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
        return $"{duration.Seconds}s";
    }
}
