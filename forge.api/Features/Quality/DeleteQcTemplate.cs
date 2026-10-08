using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

public record DeleteQcTemplateCommand(int Id) : IRequest;

public class DeleteQcTemplateHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteQcTemplateCommand>
{
    public async Task Handle(DeleteQcTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.QcChecklistTemplates
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Checklist template {request.Id} not found.");

        var inProgress = await db.QcInspections
            .CountAsync(i => i.TemplateId == template.Id && i.Status == "InProgress", cancellationToken);
        if (inProgress > 0)
            throw new InvalidOperationException(
                $"Checklist template {template.Name} is used by {inProgress} inspection{(inProgress == 1 ? "" : "s")} in progress. Finish or fail {(inProgress == 1 ? "it" : "them")} before deleting the template.");

        template.DeletedAt = clock.UtcNow;
        db.LogActivityAt("deleted", $"Deleted checklist template {template.Name}", ("QcTemplate", template.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
