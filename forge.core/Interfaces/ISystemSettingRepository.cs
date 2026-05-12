using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface ISystemSettingRepository
{
    Task<List<SystemSetting>> GetAllAsync(CancellationToken ct);
    Task<SystemSetting?> FindByKeyAsync(string key, CancellationToken ct);
    Task AddAsync(SystemSetting setting, CancellationToken ct);
    Task UpsertAsync(string key, string value, string? description, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
