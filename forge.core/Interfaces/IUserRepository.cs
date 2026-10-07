using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IUserRepository
{
    Task<List<UserResponseModel>> GetAllActiveAsync(
        Func<EmployeeProfile?, string?, bool> canBeAssignedJobs, CancellationToken ct);
    Task<List<UserResponseModel>> FindByNamesAsync(
        IEnumerable<string> names, Func<EmployeeProfile?, string?, bool> canBeAssignedJobs, CancellationToken ct);
}
