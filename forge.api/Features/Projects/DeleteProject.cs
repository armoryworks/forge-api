using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record DeleteProjectCommand(int Id) : IRequest;

public class DeleteProjectHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteProjectCommand>
{
    public async Task Handle(DeleteProjectCommand command, CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project {command.Id} not found");

        project.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
