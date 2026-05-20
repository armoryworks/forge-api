using Microsoft.EntityFrameworkCore;
using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Dashboard;

public record GetDashboardQuery : IRequest<DashboardResponseModel>;

public class GetDashboardHandler(IDashboardRepository repo, AppDbContext db) : IRequestHandler<GetDashboardQuery, DashboardResponseModel>
{
    public async Task<DashboardResponseModel> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        var data = await repo.GetDashboardDataAsync(cancellationToken);

        // Onboarding "Getting Started" completion counts. Cheap COUNTs; the
        // global soft-delete query filter excludes deleted rows. Surfaced so
        // the dashboard banner keys step completion off real customer /
        // track-type counts instead of unrelated kanban stage counts.
        var customerCount = await db.Customers.CountAsync(cancellationToken);
        var trackTypeCount = await db.TrackTypes.CountAsync(cancellationToken);

        if (data.ProductionTrack is null)
            return EmptyDashboard(customerCount, trackTypeCount);

        var stages = data.ProductionTrack.Stages
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToList();

        var totalStages = stages.Count;

        // Build task list
        var tasks = data.Jobs.Select((job, index) =>
        {
            var stage = job.CurrentStage;
            var stageIndex = stages.FindIndex(s => s.Id == stage.Id);
            var (status, statusColor) = DeriveStatus(stageIndex, totalStages, job.DueDate, today);
            var assignee = GetAssigneeInfo(job.AssigneeId, data.Users);
            var time = GenerateDisplayTime(index);

            return new DashboardTaskResponseModel(
                job.Id,
                time,
                job.Title,
                job.JobNumber,
                stage.Color,
                assignee,
                status,
                statusColor);
        }).ToList();

        // Stage counts
        var jobsByStage = data.Jobs.GroupBy(j => j.CurrentStageId).ToDictionary(g => g.Key, g => g.Count());
        var maxJobsPerStage = jobsByStage.Values.DefaultIfEmpty(0).Max();
        var stageCounts = stages.Select(s => new StageCountResponseModel(
            s.Name,
            jobsByStage.GetValueOrDefault(s.Id, 0),
            s.Color,
            Math.Max(maxJobsPerStage, 1)
        )).ToList();

        // Team members
        var teamMembers = data.Jobs
            .Where(j => j.AssigneeId.HasValue)
            .GroupBy(j => j.AssigneeId!.Value)
            .Select(g =>
            {
                var user = data.Users.GetValueOrDefault(g.Key);
                return new TeamMemberResponseModel(
                    user?.Initials ?? "??",
                    user is not null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown",
                    user?.AvatarColor ?? "#94a3b8",
                    g.Count(),
                    Math.Max(g.Count(), 5));
            })
            .OrderByDescending(t => t.TaskCount)
            .ToList();

        // Activity log - last 5 entries
        var activity = data.RecentActivity.Select(a =>
        {
            var (icon, iconColor) = GetActivityIcon(a.Action);
            var userName = a.UserId.HasValue && data.Users.TryGetValue(a.UserId.Value, out var user)
                ? user.FirstName
                : "System";
            var cleanDescription = System.Text.RegularExpressions.Regex.Replace(
                a.Description, @"@\[([^\]]+)\]\(user:\d+\)", "@$1");
            var text = $"{userName} {cleanDescription}";
            var time = FormatRelativeTime(a.CreatedAt, now);

            return new ActivityEntryResponseModel(icon, iconColor, text, time);
        }).ToList();

        // Deadlines - jobs due in next 14 days
        var deadlineCutoff = today.AddDays(14);
        var deadlines = data.Jobs
            .Where(j => j.DueDate.HasValue && j.DueDate.Value.Date <= deadlineCutoff)
            .OrderBy(j => j.DueDate)
            .Select(j => new DeadlineEntryResponseModel(
                j.DueDate!.Value.ToString("MMM d"),
                j.JobNumber,
                j.Title,
                j.DueDate.Value.Date < today))
            .ToList();

        // KPIs
        var activeCount = data.Jobs.Count;
        var overdueCount = data.Jobs.Count(j => j.DueDate.HasValue && j.DueDate.Value.Date < today);

        // Completed this week (ISO week: Monday to Sunday)
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday) startOfWeek = startOfWeek.AddDays(-7);
        var completedThisWeek = data.Jobs.Count(j => j.CompletedDate.HasValue && j.CompletedDate.Value.Date >= startOfWeek);

        // Total hours this week from time entries
        var weekStart = DateOnly.FromDateTime(startOfWeek);
        var weekEnd = DateOnly.FromDateTime(today);
        var totalMinutes = await db.TimeEntries
            .Where(t => t.Date >= weekStart && t.Date <= weekEnd)
            .SumAsync(t => t.DurationMinutes, cancellationToken);
        var totalHoursValue = Math.Round((decimal)totalMinutes / 60, 1);
        var hoursLabel = totalHoursValue >= 1 ? $"{totalHoursValue}h" : $"{totalMinutes}m";

        var kpis = new DashboardKPIsResponseModel(
            activeCount,
            completedThisWeek,
            overdueCount,
            0,
            hoursLabel,
            totalHoursValue > 0 ? "up" : "neutral");

        return new DashboardResponseModel(tasks, stageCounts, teamMembers, activity, deadlines, kpis,
            customerCount, trackTypeCount);
    }

    private static DashboardResponseModel EmptyDashboard(int customerCount = 0, int trackTypeCount = 0) =>
        new([], [], [], [], [],
            new DashboardKPIsResponseModel(0, 0, 0, 0, "0h", "neutral"),
            customerCount, trackTypeCount);

    private static string GenerateDisplayTime(int index)
    {
        var totalMinutes = 8 * 60 + index * 30;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var period = hours >= 12 ? "p" : "a";
        var displayHour = hours > 12 ? hours - 12 : hours;
        if (displayHour == 0) displayHour = 12;

        return $"{displayHour}:{minutes:D2}{period}";
    }

    private static (string Status, string StatusColor) DeriveStatus(int stageIndex, int totalStages, DateTimeOffset? dueDate, DateTimeOffset today)
    {
        if (dueDate.HasValue && dueDate.Value.Date < today)
            return ("LATE", "#ef4444");

        if (totalStages <= 1)
            return ("ACTIVE", "#3b82f6");

        var position = (double)stageIndex / (totalStages - 1);

        return position switch
        {
            < 0.34 => ("NEXT", "#94a3b8"),
            < 0.67 => ("ACTIVE", "#3b82f6"),
            _ => ("FINISHING", "#22c55e")
        };
    }

    private static AssigneeInfo GetAssigneeInfo(int? assigneeId, Dictionary<int, ApplicationUserInfo> users)
    {
        if (assigneeId.HasValue && users.TryGetValue(assigneeId.Value, out var user))
            return new AssigneeInfo(user.Initials ?? "??", user.AvatarColor ?? "#94a3b8");

        return new AssigneeInfo("--", "#cbd5e1");
    }

    private static (string Icon, string IconColor) GetActivityIcon(ActivityAction action) => action switch
    {
        ActivityAction.Created => ("add_circle", "#22c55e"),
        ActivityAction.StageMoved => ("swap_horiz", "#3b82f6"),
        ActivityAction.FieldChanged => ("edit", "#f59e0b"),
        ActivityAction.Assigned => ("person_add", "#8b5cf6"),
        ActivityAction.Unassigned => ("person_remove", "#94a3b8"),
        ActivityAction.SubtaskAdded => ("checklist", "#06b6d4"),
        ActivityAction.SubtaskCompleted => ("task_alt", "#22c55e"),
        ActivityAction.CommentAdded => ("comment", "#6366f1"),
        ActivityAction.Archived => ("archive", "#64748b"),
        ActivityAction.Restored => ("unarchive", "#f97316"),
        _ => ("info", "#94a3b8")
    };

    private static string FormatRelativeTime(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var diff = now - timestamp;

        return diff.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => $"{(int)diff.TotalMinutes}m ago",
            < 1440 => $"{(int)diff.TotalHours}h ago",
            < 10080 => $"{(int)diff.TotalDays}d ago",
            _ => timestamp.ToString("MMM d")
        };
    }
}
