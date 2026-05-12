using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Definitions;

/// <summary>Workflow Pattern Phase 3 — list workflow definitions, optional entityType filter.</summary>
public record ListWorkflowDefinitionsQuery(string? EntityType)
    : IRequest<IReadOnlyList<WorkflowDefinitionResponseModel>>;

public class ListWorkflowDefinitionsHandler(AppDbContext db)
    : IRequestHandler<ListWorkflowDefinitionsQuery, IReadOnlyList<WorkflowDefinitionResponseModel>>
{
    public async Task<IReadOnlyList<WorkflowDefinitionResponseModel>> Handle(
        ListWorkflowDefinitionsQuery request, CancellationToken ct)
    {
        var query = db.WorkflowDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(d => d.EntityType == request.EntityType);

        var rows = await query
            .OrderBy(d => d.EntityType)
            .ThenBy(d => d.DefinitionId)
            .ToListAsync(ct);

        return rows.Select(d => new WorkflowDefinitionResponseModel(
            d.Id,
            d.DefinitionId,
            d.EntityType,
            d.DefaultMode,
            d.StepsJson,
            d.ExpressTemplateComponent,
            d.IsSeedData)).ToList();
    }
}
