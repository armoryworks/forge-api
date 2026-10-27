using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Projects;

public record UnlinkJobFromWbsCommand(int ProjectId, int ElementId, int JobId) : IRequest;

public class UnlinkJobFromWbsHandler(AppDbContext db) : IRequestHandler<UnlinkJobFromWbsCommand>
{
    public async Task Handle(UnlinkJobFromWbsCommand command, CancellationToken cancellationToken)
    {
        var link = await db.WbsElementJobs
            .Include(l => l.WbsElement)
            .FirstOrDefaultAsync(l => l.WbsElementId == command.ElementId && l.JobId == command.JobId
                && l.WbsElement.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {command.JobId} is not linked to WBS element {command.ElementId}");

        db.WbsElementJobs.Remove(link);
        db.LogActivityAt("project-unlinked", $"Unlinked from project WBS element {link.WbsElement.Code} {link.WbsElement.Name}", ("Job", command.JobId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
