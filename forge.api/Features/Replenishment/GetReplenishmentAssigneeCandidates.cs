using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record GetReplenishmentAssigneeCandidatesQuery : IRequest<List<ReplenishmentAssigneeCandidateResponseModel>>;

public class GetReplenishmentAssigneeCandidatesHandler(AppDbContext db)
    : IRequestHandler<GetReplenishmentAssigneeCandidatesQuery, List<ReplenishmentAssigneeCandidateResponseModel>>
{
    private static readonly string[] ApproverRoles = ["Admin", "Manager"];

    public async Task<List<ReplenishmentAssigneeCandidateResponseModel>> Handle(
        GetReplenishmentAssigneeCandidatesQuery request, CancellationToken cancellationToken)
    {
        var approverIds = db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => ApproverRoles.Contains(x.Name!))
            .Select(x => x.UserId);

        var users = await db.Users
            .Where(u => u.IsActive && approverIds.Contains(u.Id))
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new ReplenishmentAssigneeCandidateResponseModel(u.Id, $"{u.FirstName} {u.LastName}".Trim()))
            .ToList();
    }
}
