using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record TeamModel(int Id, string Name, string? Color, string? Description, int MemberCount);

public record GetTeamsQuery : IRequest<List<TeamModel>>;

public class GetTeamsHandler(AppDbContext db) : IRequestHandler<GetTeamsQuery, List<TeamModel>>
{
    public async Task<List<TeamModel>> Handle(GetTeamsQuery request, CancellationToken ct)
    {
        var teams = await db.Teams
            .Where(t => t.IsActive && t.DeletedAt == null)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var memberCounts = await db.Users
            .Where(u => u.IsActive && u.TeamId != null)
            .GroupBy(u => u.TeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId!.Value, x => x.Count, ct);

        return teams.Select(t => new TeamModel(
            t.Id,
            t.Name,
            t.Color,
            t.Description,
            memberCounts.TryGetValue(t.Id, out var count) ? count : 0
        )).ToList();
    }
}
