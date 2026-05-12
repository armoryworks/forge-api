using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.EntityCapabilityRequirements;

/// <summary>
/// Admin delete by id. Soft-delete via the global filter on BaseEntity —
/// the row is marked DeletedAt rather than physically removed, so audit
/// log lookups can still resolve historical rows.
/// </summary>
public record DeleteEntityCapabilityRequirementCommand(int Id) : IRequest<Unit>;

public class DeleteEntityCapabilityRequirementHandler(AppDbContext db)
    : IRequestHandler<DeleteEntityCapabilityRequirementCommand, Unit>
{
    public async Task<Unit> Handle(
        DeleteEntityCapabilityRequirementCommand request,
        CancellationToken cancellationToken)
    {
        var row = await db.EntityCapabilityRequirements
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"EntityCapabilityRequirement {request.Id} not found");

        db.EntityCapabilityRequirements.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
