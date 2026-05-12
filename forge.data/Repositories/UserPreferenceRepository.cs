using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class UserPreferenceRepository(AppDbContext db) : IUserPreferenceRepository
{
    public async Task<List<UserPreferenceResponseModel>> GetByUserIdAsync(int userId, CancellationToken ct)
    {
        return await db.UserPreferences
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Key)
            .Select(p => new UserPreferenceResponseModel(p.Key, p.ValueJson))
            .ToListAsync(ct);
    }

    public async Task<UserPreference?> FindByKeyAsync(int userId, string key, CancellationToken ct)
    {
        return await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, ct);
    }

    public async Task AddAsync(UserPreference preference, CancellationToken ct)
    {
        await db.UserPreferences.AddAsync(preference, ct);
    }

    public Task RemoveAsync(UserPreference preference)
    {
        db.UserPreferences.Remove(preference);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
    }
}
