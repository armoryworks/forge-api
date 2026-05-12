using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IUserRepository
{
    Task<List<UserResponseModel>> GetAllActiveAsync(CancellationToken ct);
    Task<List<UserResponseModel>> FindByNamesAsync(IEnumerable<string> names, CancellationToken ct);
}
