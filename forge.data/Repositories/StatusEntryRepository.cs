using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class StatusEntryRepository(AppDbContext db) : IStatusEntryRepository
{
    public async Task<List<StatusEntryResponseModel>> GetHistoryAsync(string entityType, int entityId, CancellationToken ct)
    {
        var entries = await db.StatusEntries
            .Where(s => s.EntityType == entityType && s.EntityId == entityId)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

        var userIds = entries.Where(e => e.SetById.HasValue).Select(e => e.SetById!.Value).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return entries.Select(e => ToResponseModel(e, users)).ToList();
    }

    public async Task<StatusEntryResponseModel?> GetCurrentWorkflowStatusAsync(string entityType, int entityId, CancellationToken ct)
    {
        var entry = await db.StatusEntries
            .Where(s => s.EntityType == entityType && s.EntityId == entityId && s.Category == "workflow" && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (entry is null) return null;

        var user = entry.SetById.HasValue
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == entry.SetById.Value, ct)
            : null;

        var users = user is not null
            ? new Dictionary<int, ApplicationUser> { { user.Id, user } }
            : new Dictionary<int, ApplicationUser>();

        return ToResponseModel(entry, users);
    }

    public async Task<List<StatusEntryResponseModel>> GetActiveHoldsAsync(string entityType, int entityId, CancellationToken ct)
    {
        var entries = await db.StatusEntries
            .Where(s => s.EntityType == entityType && s.EntityId == entityId && s.Category == "hold" && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);

        var userIds = entries.Where(e => e.SetById.HasValue).Select(e => e.SetById!.Value).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return entries.Select(e => ToResponseModel(e, users)).ToList();
    }

    public Task<StatusEntry?> FindAsync(int id, CancellationToken ct)
        => db.StatusEntries.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(StatusEntry entry, CancellationToken ct)
    {
        await db.StatusEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);

    private static StatusEntryResponseModel ToResponseModel(StatusEntry entry, Dictionary<int, ApplicationUser> users)
    {
        var setByName = entry.SetById.HasValue && users.TryGetValue(entry.SetById.Value, out var user)
            ? $"{user.FirstName} {user.LastName}"
            : null;

        return new StatusEntryResponseModel(
            entry.Id,
            entry.EntityType,
            entry.EntityId,
            entry.StatusCode,
            entry.StatusLabel,
            entry.Category,
            entry.StartedAt,
            entry.EndedAt,
            entry.Notes,
            entry.SetById,
            setByName,
            entry.CreatedAt);
    }
}
