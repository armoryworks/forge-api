using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SystemSettingRepository(AppDbContext db) : ISystemSettingRepository
{
    public async Task<List<SystemSetting>> GetAllAsync(CancellationToken ct)
        => await db.SystemSettings.OrderBy(s => s.Key).ToListAsync(ct);

    public async Task<SystemSetting?> FindByKeyAsync(string key, CancellationToken ct)
        => await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);

    public async Task AddAsync(SystemSetting setting, CancellationToken ct)
        => await db.SystemSettings.AddAsync(setting, ct);

    public async Task UpsertAsync(string key, string value, string? description, CancellationToken ct)
    {
        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing != null)
        {
            existing.Value = value;
            if (description != null) existing.Description = description;
        }
        else
        {
            await db.SystemSettings.AddAsync(new SystemSetting { Key = key, Value = value, Description = description }, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
        => await db.SaveChangesAsync(ct);
}
