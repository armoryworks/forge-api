using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record DeactivateUserCommand(int UserId) : IRequest;

public class DeactivateUserHandler(AppDbContext db, ISessionStore sessionStore)
    : IRequestHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([request.UserId], ct)
            ?? throw new KeyNotFoundException($"User {request.UserId} not found");

        user.IsActive = false;

        // Unassign from all active jobs
        var assignedJobs = await db.Jobs
            .Where(j => j.AssigneeId == request.UserId && !j.IsArchived && j.CompletedDate == null)
            .ToListAsync(ct);

        foreach (var job in assignedJobs)
        {
            job.AssigneeId = null;
        }

        await db.SaveChangesAsync(ct);

        // Revoke all sessions — force immediate logout
        await sessionStore.RevokeAllUserSessionsAsync(request.UserId, ct);
    }
}
