using MediatR;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

using ApplicationUser = Forge.Data.Context.ApplicationUser;

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
    string Role,
    bool OpenFromPriorShift = false,
    DateTimeOffset? OpenSince = null);

public record GetClockStatusQuery(int? TeamId = null) : IRequest<List<ClockWorkerModel>>;

public class GetClockStatusHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IClockEventTypeService clockEventTypeService,
    IClock clock)
    : IRequestHandler<GetClockStatusQuery, List<ClockWorkerModel>>
{
    public async Task<List<ClockWorkerModel>> Handle(GetClockStatusQuery request, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var shopTimeZone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var dayStartUtc = ClockStateRules.DayStartUtc(shopTimeZone, now);
        var shopToday = ClockStateRules.LocalToday(shopTimeZone, now);

        var usersQuery = db.Users.Where(u => u.IsActive);
        if (request.TeamId.HasValue)
            usersQuery = usersQuery.Where(u => u.TeamId == request.TeamId.Value || u.TeamId == null);

        var users = await usersQuery
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Email, u.Initials, u.AvatarColor })
            .ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();

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

        var eventMap = await ClockStateRules.LatestEventsAsync(db, userIds, now, ct: ct);

        // Get active timers for all users
        var activeTimers = await db.TimeEntries
            .Include(t => t.Job)
            .Where(t => t.TimerStart != null && t.TimerStop == null)
            .ToListAsync(ct);

        var timersByUser = activeTimers
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.TimerStart).First());

        // Load assigned jobs in shop-floor stages only (physical work, not admin/office stages)
        var assignedJobs = await db.Jobs
            .Include(j => j.CurrentStage)
            .Where(j => j.AssigneeId.HasValue
                && userIds.Contains(j.AssigneeId.Value)
                && j.CurrentStage.IsShopFloor
                && !j.IsArchived
                && j.CompletedDate == null
                && j.Disposition == null)
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
                    ClockStateRules.IsOverdue(j.DueDate, shopToday),
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

        return users.Select(u =>
        {
            var hasEvent = eventMap.TryGetValue(u.Id, out var evt);
            var (status, countsAsActive) = ClockStateRules.ResolveStatus(evt, eventTypeDefs);
            var openFromPriorShift = ClockStateRules.IsOpenFromPriorShift(evt, countsAsActive, dayStartUtc, now);

            timersByUser.TryGetValue(u.Id, out var timer);
            var isWorking = status == ClockStateRules.StatusIn;
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
                userRoles.GetValueOrDefault(u.Id, "ProductionWorker"),
                openFromPriorShift,
                openFromPriorShift ? evt!.Timestamp : null);
        }).OrderBy(w => w.Status == ClockStateRules.StatusOut).ThenBy(w => w.Name).ToList();
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
