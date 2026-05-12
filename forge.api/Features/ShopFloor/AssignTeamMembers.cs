using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record AssignTeamMembersCommand(int TeamId, List<int> UserIds) : IRequest;

public class AssignTeamMembersHandler(AppDbContext db) : IRequestHandler<AssignTeamMembersCommand>
{
    public async Task Handle(AssignTeamMembersCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId && t.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Team {request.TeamId} not found");

        // Remove current members not in the new list
        var currentMembers = await db.Users.Where(u => u.TeamId == request.TeamId).ToListAsync(ct);
        foreach (var user in currentMembers)
        {
            if (!request.UserIds.Contains(user.Id))
                user.TeamId = null;
        }

        // Assign new members
        var usersToAssign = await db.Users
            .Where(u => request.UserIds.Contains(u.Id))
            .ToListAsync(ct);

        foreach (var user in usersToAssign)
            user.TeamId = request.TeamId;

        await db.SaveChangesAsync(ct);
    }
}
