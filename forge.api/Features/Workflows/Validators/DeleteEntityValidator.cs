using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Workflows;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Validators;

/// <summary>
/// Workflow Pattern Phase 3 — Soft-delete an entity readiness validator.
/// Refuses if any seeded definition's StepsJson references this validator
/// id (best-effort string match — admin can still flip
/// <c>IsSeedData</c> off on the definition first if a redesign is needed).
/// Seeded validator rows refuse delete by default to keep installs in a
/// consistent baseline; admins can remove the <c>IsSeedData</c> flag via
/// the update endpoint to override.
/// </summary>
public record DeleteEntityValidatorCommand(int Id) : IRequest<Unit>;

public class DeleteEntityValidatorHandler(AppDbContext db)
    : IRequestHandler<DeleteEntityValidatorCommand, Unit>
{
    public async Task<Unit> Handle(DeleteEntityValidatorCommand request, CancellationToken ct)
    {
        var row = await db.EntityReadinessValidators
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Validator id {request.Id} not found.");

        if (row.IsSeedData)
            throw new InvalidOperationException(
                "Seeded validators cannot be deleted. Override IsSeedData via update first if absolutely necessary.");

        // Reference check: any non-deleted definition for the same entity type
        // whose StepsJson references this validator id?
        var refKey = $"\"{row.ValidatorId}\"";
        var defs = await db.WorkflowDefinitions
            .Where(d => d.EntityType == row.EntityType && d.StepsJson.Contains(refKey))
            .Select(d => d.DefinitionId)
            .ToListAsync(ct);
        if (defs.Count > 0)
            throw new InvalidOperationException(
                $"Validator '{row.ValidatorId}' is referenced by workflow definitions: {string.Join(", ", defs)}.");

        row.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
