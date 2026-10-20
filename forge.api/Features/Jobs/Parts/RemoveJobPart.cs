using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Middleware;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Parts;

public record RemoveJobPartCommand(int JobId, int JobPartId) : IRequest;

public class RemoveJobPartHandler(AppDbContext db, IHttpContextAccessor? httpContext = null) : IRequestHandler<RemoveJobPartCommand>
{
    public const string BuildPartRoleMessage = "Only an admin or a manager can remove the part this work order builds.";

    public async Task Handle(RemoveJobPartCommand request, CancellationToken cancellationToken)
    {
        var jobPart = await db.JobParts
            .Include(jp => jp.Job)
            .FirstOrDefaultAsync(jp => jp.Id == request.JobPartId && jp.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job part with ID {request.JobPartId} not found.");

        if (jobPart.PartId == jobPart.Job.PartId)
        {
            var user = httpContext?.HttpContext?.User;
            if (user?.IsInRole("Admin") != true && user?.IsInRole("Manager") != true)
                throw new ForbiddenException(BuildPartRoleMessage);
            await UpdateJobHandler.EnsureQuantityEditableAsync(db, jobPart.Job, cancellationToken);
        }

        db.JobParts.Remove(jobPart);
        await db.SaveChangesAsync(cancellationToken);
    }
}
