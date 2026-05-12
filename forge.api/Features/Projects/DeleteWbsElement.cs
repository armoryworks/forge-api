using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record DeleteWbsElementCommand(int ProjectId, int ElementId) : IRequest;

public class DeleteWbsElementHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteWbsElementCommand>
{
    public async Task Handle(DeleteWbsElementCommand command, CancellationToken cancellationToken)
    {
        var element = await db.WbsElements
            .Include(e => e.ChildElements)
            .FirstOrDefaultAsync(e => e.Id == command.ElementId && e.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"WBS element {command.ElementId} not found in project {command.ProjectId}");

        if (element.ChildElements.Any(c => c.DeletedAt == null))
            throw new InvalidOperationException("Cannot delete WBS element with active child elements");

        element.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
