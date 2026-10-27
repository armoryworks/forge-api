using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Projects;

/// <summary>Links a job to a WBS element of the project; linking the same job to the same element again is a no-op.</summary>
public record LinkJobToWbsCommand(int ProjectId, int ElementId, LinkJobToWbsRequestModel Request) : IRequest;

public class LinkJobToWbsHandler(AppDbContext db, IClock clock, IHttpContextAccessor httpContext) : IRequestHandler<LinkJobToWbsCommand>
{
    public async Task Handle(LinkJobToWbsCommand command, CancellationToken cancellationToken)
    {
        var element = await db.WbsElements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == command.ElementId && e.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"WBS element {command.ElementId} not found in project {command.ProjectId}");

        if (!await db.Jobs.AnyAsync(j => j.Id == command.Request.JobId, cancellationToken))
            throw new KeyNotFoundException($"Job {command.Request.JobId} not found");

        if (await db.WbsElementJobs.AnyAsync(l => l.WbsElementId == command.ElementId && l.JobId == command.Request.JobId, cancellationToken))
            return;

        db.WbsElementJobs.Add(new WbsElementJob
        {
            WbsElementId = command.ElementId,
            JobId = command.Request.JobId,
            LinkedById = int.TryParse(httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null,
            CreatedAt = clock.UtcNow,
        });
        db.LogActivityAt("project-linked", $"Linked to project WBS element {element.Code} {element.Name}", ("Job", command.Request.JobId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
