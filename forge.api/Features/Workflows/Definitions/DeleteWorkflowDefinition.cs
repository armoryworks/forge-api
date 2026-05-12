using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Definitions;

/// <summary>
/// Workflow Pattern Phase 3 — admin soft-delete. Refuses if any in-flight
/// (not completed, not abandoned) workflow_runs reference this definition.
/// </summary>
public record DeleteWorkflowDefinitionCommand(string DefinitionId) : IRequest<Unit>;

public class DeleteWorkflowDefinitionHandler(AppDbContext db)
    : IRequestHandler<DeleteWorkflowDefinitionCommand, Unit>
{
    public async Task<Unit> Handle(DeleteWorkflowDefinitionCommand request, CancellationToken ct)
    {
        var row = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.DefinitionId == request.DefinitionId, ct)
            ?? throw new KeyNotFoundException($"Workflow definition '{request.DefinitionId}' not found.");

        var inFlight = await db.WorkflowRuns
            .Where(r => r.DefinitionId == request.DefinitionId
                        && r.CompletedAt == null
                        && r.AbandonedAt == null)
            .CountAsync(ct);
        if (inFlight > 0)
            throw new InvalidOperationException(
                $"Cannot delete '{request.DefinitionId}' — {inFlight} in-flight run(s) still reference it.");

        row.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
