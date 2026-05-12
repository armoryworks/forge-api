using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IUserPreferenceRepository
{
    Task<List<UserPreferenceResponseModel>> GetByUserIdAsync(int userId, CancellationToken ct);
    Task<UserPreference?> FindByKeyAsync(int userId, string key, CancellationToken ct);
    Task AddAsync(UserPreference preference, CancellationToken ct);
    Task RemoveAsync(UserPreference preference);
    Task SaveChangesAsync(CancellationToken ct);
}
