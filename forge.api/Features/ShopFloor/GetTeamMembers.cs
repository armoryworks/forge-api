using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record TeamMemberModel(int Id, string FirstName, string LastName, string? Initials, string? AvatarColor, string Email, bool IsActive);

public record GetTeamMembersQuery(int TeamId) : IRequest<List<TeamMemberModel>>;

public class GetTeamMembersHandler(AppDbContext db) : IRequestHandler<GetTeamMembersQuery, List<TeamMemberModel>>
{
    public async Task<List<TeamMemberModel>> Handle(GetTeamMembersQuery request, CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.TeamId == request.TeamId)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new TeamMemberModel(u.Id, u.FirstName, u.LastName, u.Initials, u.AvatarColor, u.Email ?? string.Empty, u.IsActive))
            .ToListAsync(ct);
    }
}
