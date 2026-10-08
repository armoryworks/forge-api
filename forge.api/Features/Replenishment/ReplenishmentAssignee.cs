using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public static class ReplenishmentAssignee
{
    public const string SettingKey = "replenishment.assignee_user_id";
    public const string TaskSourceEntityType = "ReorderSuggestion";

    public static int? Parse(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;

    public static async Task<int?> ResolveActiveAsync(AppDbContext db, CancellationToken ct)
    {
        var stored = await db.SystemSettings
            .Where(s => s.Key == SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        if (Parse(stored) is not int userId)
            return null;

        var active = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, ct);
        return active ? userId : null;
    }

    public static async Task CloseTasksAsync(
        AppDbContext db,
        IReadOnlyCollection<int> suggestionIds,
        FollowUpStatus status,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (suggestionIds.Count == 0)
            return;

        var tasks = await db.FollowUpTasks
            .Where(t => t.SourceEntityType == TaskSourceEntityType
                && suggestionIds.Contains(t.SourceEntityId)
                && t.Status == FollowUpStatus.Open)
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            task.Status = status;
            if (status == FollowUpStatus.Completed)
                task.CompletedAt = now;
            else
                task.DismissedAt = now;
        }
    }
}
