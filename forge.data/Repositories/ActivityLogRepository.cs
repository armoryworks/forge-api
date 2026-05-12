using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class ActivityLogRepository(AppDbContext db) : IActivityLogRepository
{
    public async Task<List<ActivityResponseModel>> GetByJobIdAsync(int jobId, CancellationToken ct)
    {
        var logs = await db.JobActivityLogs
            .Where(l => l.JobId == jobId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var userIds = logs
            .Where(l => l.UserId.HasValue)
            .Select(l => l.UserId!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct)
            : [];

        return logs.Select(l =>
        {
            var user = l.UserId.HasValue && users.TryGetValue(l.UserId.Value, out var u) ? u : null;
            return new ActivityResponseModel(
                l.Id,
                l.Action.ToString(),
                l.FieldName,
                l.OldValue,
                l.NewValue,
                l.Description,
                user?.Initials,
                user is not null ? $"{user.FirstName} {user.LastName}".Trim() : null,
                l.CreatedAt);
        }).ToList();
    }

    public async Task<List<ActivityResponseModel>> GetByEntityAsync(string entityType, int entityId, CancellationToken ct)
    {
        var logs = await db.ActivityLogs
            .Where(l => l.EntityType == entityType && l.EntityId == entityId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var userIds = logs
            .Where(l => l.UserId.HasValue)
            .Select(l => l.UserId!.Value)
            .Distinct()
            .ToList();

        var users = userIds.Count > 0
            ? await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, ct)
            : [];

        return logs.Select(l =>
        {
            var user = l.UserId.HasValue && users.TryGetValue(l.UserId.Value, out var u) ? u : null;
            return new ActivityResponseModel(
                l.Id,
                l.Action,
                l.FieldName,
                l.OldValue,
                l.NewValue,
                l.Description,
                user?.Initials,
                user is not null ? $"{user.FirstName} {user.LastName}".Trim() : null,
                l.CreatedAt);
        }).ToList();
    }

    public async Task<bool> JobExistsAsync(int jobId, CancellationToken ct)
    {
        return await db.Jobs.AnyAsync(j => j.Id == jobId, ct);
    }

    public Task AddAsync(JobActivityLog log, CancellationToken ct)
    {
        db.JobActivityLogs.Add(log);
        return Task.CompletedTask;
    }

    public Task AddAsync(ActivityLog log, CancellationToken ct)
    {
        db.ActivityLogs.Add(log);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
