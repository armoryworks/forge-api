using MediatR;
using Forge.Api.Capabilities;
using Forge.Api.Features.EmployeeProfile;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Users;

public record GetUsersQuery : IRequest<List<UserResponseModel>>;

public class GetUsersHandler(
    IUserRepository repo,
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities) : IRequestHandler<GetUsersQuery, List<UserResponseModel>>
{
    public async Task<List<UserResponseModel>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var canBeAssignedJobs = await EmployeeComplianceRules.LoadAssignabilityCheckAsync(db, capabilities, cancellationToken);
        return await repo.GetAllActiveAsync(canBeAssignedJobs, cancellationToken);
    }
}
