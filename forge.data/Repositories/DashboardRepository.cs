using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class DashboardRepository(AppDbContext db) : IDashboardRepository
{
    public async Task<DashboardDataSet> GetDashboardDataAsync(CancellationToken ct)
    {
        var productionTrack = await db.TrackTypes
            .Include(t => t.Stages.Where(s => s.IsActive))
            .Where(t => t.IsDefault && t.IsActive)
            .FirstOrDefaultAsync(ct);

        if (productionTrack is null)
            return new DashboardDataSet(null, [], [], []);

        var jobs = await db.Jobs
            .Include(j => j.CurrentStage)
            .Include(j => j.Customer)
            .Where(j => j.TrackTypeId == productionTrack.Id && !j.IsArchived)
            .OrderBy(j => j.CurrentStage.SortOrder)
            .ThenBy(j => j.BoardPosition)
            .ToListAsync(ct);

        var assigneeIds = jobs
            .Where(j => j.AssigneeId.HasValue)
            .Select(j => j.AssigneeId!.Value)
            .Distinct()
            .ToList();

        var activityLogs = await db.JobActivityLogs
            .Include(a => a.Job)
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        var activityUserIds = activityLogs
            .Where(a => a.UserId.HasValue)
            .Select(a => a.UserId!.Value)
            .Distinct()
            .ToList();

        var allUserIds = assigneeIds.Union(activityUserIds).Distinct().ToList();

        var users = await db.Users
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => new ApplicationUserInfo(u.Id, u.Initials, u.FirstName, u.LastName, u.AvatarColor),
                ct);

        return new DashboardDataSet(productionTrack, jobs, users, activityLogs);
    }
}
