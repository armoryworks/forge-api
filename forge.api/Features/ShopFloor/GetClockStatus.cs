using MediatR;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;


namespace Forge.Api.Features.ShopFloor;

public record ClockWorkerModel(
    int UserId,
    string Name,
    string Email,
    string Initials,
    string AvatarColor,
    bool IsClockedIn,
    DateTimeOffset? ClockedInAt,
    string Status,
    string? CurrentTask,
    string? CurrentJobNumber,
    string TimeOnTask,
    DateTimeOffset? StatusSince,
    List<WorkerAssignmentModel> Assignments,
    string Role);

public record GetClockStatusQuery(int? TeamId = null) : IRequest<List<ClockWorkerModel>>;

public class GetClockStatusHandler(AppDbContext db, UserManager<ApplicationUser> userManager, IClockEventTypeService clockEventTypeService)
    : IRequestHandler<GetClockStatusQuery, List<ClockWorkerModel>>
{
    public async Task<List<ClockWorkerModel>> Handle(GetClockStatusQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        var usersQuery = db.Users.Where(u => u.IsActive);
        if (request.TeamId.HasValue)
            usersQuery = usersQuery.Where(u => u.TeamId == request.TeamId.Value);

        var users = await usersQuery
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Email, u.Initials, u.AvatarColor })
            .ToListAsync(ct);

        // Fetch primary role for each user (highest-privilege role wins)
        var roleOrder = new[] { "Admin", "Manager", "OfficeManager", "PM", "Engineer", "ProductionWorker" };
        var userRoles = new Dictionary<int, string>();
        foreach (var u in users)
        {
            var appUser = await userManager.FindByIdAsync(u.Id.ToString());
            if (appUser != null)
            {
                var roles = await userManager.GetRolesAsync(appUser);
                var primary = roleOrder.FirstOrDefault(r => roles.Contains(r)) ?? "ProductionWorker";
                userRoles[u.Id] = primary;
            }
            else
            {
                userRoles[u.Id] = "ProductionWorker";
            }
        }

        var latestEvents = await db.ClockEvents
            .Where(e => e.Timestamp >= today)
            .GroupBy(e => e.UserId)
            .Select(g => g.OrderByDescending(e => e.Timestamp).First())
            .ToListAsync(ct);

        var eventMap = latestEvents.ToDictionary(e => e.UserId);

        // Get active timers for all users
        var activeTimers = await db.TimeEntries
            .Include(t => t.Job)
            .Where(t => t.TimerStart != null && t.TimerStop == null)
            .ToListAsync(ct);

        var timersByUser = activeTimers.ToDictionary(t => t.UserId);

        // Load assigned jobs in shop-floor stages only (physical work, not admin/office stages)
        var userIds = users.Select(u => u.Id).ToList();
        var assignedJobs = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => j.AssigneeId.HasValue
                && userIds.Contains(j.AssigneeId.Value)
                && j.CurrentStage.IsShopFloor
                && !j.IsArchived
                && j.CompletedDate == null)
            .OrderBy(j => j.Priority)
            .ThenBy(j => j.DueDate ?? DateTimeOffset.MaxValue)
            .Select(j => new
            {
                j.Id,
                j.AssigneeId,
                j.JobNumber,
                j.Title,
                Priority = j.Priority.ToString(),
                StageName = j.CurrentStage.Name,
                StageColor = j.CurrentStage.Color ?? "#94a3b8",
                j.DueDate,
            })
            .ToListAsync(ct);

        var assignmentsByUser = assignedJobs
            .GroupBy(j => j.AssigneeId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(j => new WorkerAssignmentModel(
                    j.Id,
                    j.JobNumber,
                    j.Title,
                    j.Priority,
                    j.StageName,
                    j.StageColor,
                    j.DueDate.HasValue && j.DueDate.Value.Date < today,
                    false)).ToList());

        // Mark active timer jobs
        foreach (var timer in activeTimers)
        {
            if (timer.JobId.HasValue && assignmentsByUser.TryGetValue(timer.UserId, out var list))
            {
                var idx = list.FindIndex(a => a.JobId == timer.JobId.Value);
                if (idx >= 0)
                    list[idx] = list[idx] with { HasActiveTimer = true };
            }
        }

        // Load clock event type definitions from reference data
        var eventTypeDefs = await clockEventTypeService.GetAllAsync(ct);
        var eventTypeMap = eventTypeDefs.ToDictionary(d => d.Code);

        return users.Select(u =>
        {
            var hasEvent = eventMap.TryGetValue(u.Id, out var evt);
            var typeCode = hasEvent ? evt!.EventTypeCode : null;
            var typeDef = typeCode is not null && eventTypeMap.TryGetValue(typeCode, out var def) ? def : null;

            var status = typeDef?.StatusMapping ?? "Out";
            var countsAsActive = typeDef?.CountsAsActive ?? false;

            timersByUser.TryGetValue(u.Id, out var timer);
            var isWorking = status == "In";
            var clockInTime = isWorking && hasEvent ? evt!.Timestamp : (DateTimeOffset?)null;

            DateTimeOffset? statusSince = null;
            var timeOnTask = "";
            if (isWorking && timer?.TimerStart != null)
            {
                statusSince = timer.TimerStart.Value;
                timeOnTask = FormatDuration(now - statusSince.Value);
            }
            else if (isWorking && clockInTime.HasValue)
            {
                statusSince = clockInTime.Value;
                timeOnTask = FormatDuration(now - statusSince.Value);
            }
            else if (countsAsActive && !isWorking && hasEvent)
            {
                statusSince = evt!.Timestamp;
                timeOnTask = FormatDuration(now - statusSince.Value);
            }

            assignmentsByUser.TryGetValue(u.Id, out var userAssignments);

            return new ClockWorkerModel(
                u.Id,
                u.Name,
                u.Email ?? "",
                u.Initials ?? "??",
                u.AvatarColor ?? "#94a3b8",
                countsAsActive,
                clockInTime,
                status,
                timer?.Job?.Title,
                timer?.Job?.JobNumber,
                timeOnTask,
                statusSince,
                userAssignments ?? [],
                userRoles.GetValueOrDefault(u.Id, "ProductionWorker"));
        }).OrderBy(w => w.Status == "Out").ThenBy(w => w.Name).ToList();
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
