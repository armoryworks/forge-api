using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.ShopFloor;

public record DeleteTeamCommand(int Id) : IRequest;

public class DeleteTeamHandler(AppDbContext db) : IRequestHandler<DeleteTeamCommand>
{
    public async Task Handle(DeleteTeamCommand request, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.Id && t.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Team {request.Id} not found");

        // Check for active kiosk terminals
        var hasTerminals = await db.KioskTerminals.AnyAsync(k => k.TeamId == team.Id && k.IsActive, ct);
        if (hasTerminals)
            throw new InvalidOperationException("Cannot delete a team with active kiosk terminals. Deactivate terminals first.");

        // Unassign users from this team
        var assignedUsers = await db.Users.Where(u => u.TeamId == team.Id).ToListAsync(ct);
        foreach (var user in assignedUsers)
            user.TeamId = null;

        team.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
