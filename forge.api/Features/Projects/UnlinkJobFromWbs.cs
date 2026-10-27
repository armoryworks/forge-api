using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record UnlinkJobFromWbsCommand(int ProjectId, int ElementId, int JobId) : IRequest;

public class UnlinkJobFromWbsHandler(AppDbContext db) : IRequestHandler<UnlinkJobFromWbsCommand>
{
    public async Task Handle(UnlinkJobFromWbsCommand command, CancellationToken cancellationToken)
    {
        var link = await db.WbsElementJobs
            .FirstOrDefaultAsync(l => l.WbsElementId == command.ElementId && l.JobId == command.JobId
                && l.WbsElement.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {command.JobId} is not linked to WBS element {command.ElementId}");

        db.WbsElementJobs.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
    }
}
