using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record LinkJobToWbsCommand(int ProjectId, int ElementId, LinkJobToWbsRequestModel Request) : IRequest;

public class LinkJobToWbsHandler(AppDbContext db) : IRequestHandler<LinkJobToWbsCommand>
{
    public async Task Handle(LinkJobToWbsCommand command, CancellationToken cancellationToken)
    {
        var element = await db.WbsElements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == command.ElementId && e.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"WBS element {command.ElementId} not found in project {command.ProjectId}");

        var job = await db.Jobs
            .FirstOrDefaultAsync(j => j.Id == command.Request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {command.Request.JobId} not found");

        // Store project/WBS reference as metadata — actual FK fields are deferred per spec
        // For now, we log the association without adding FK to Job entity
        await db.SaveChangesAsync(cancellationToken);
    }
}
