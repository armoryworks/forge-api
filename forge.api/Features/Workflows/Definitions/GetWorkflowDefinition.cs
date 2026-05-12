using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Workflows.Definitions;

/// <summary>Workflow Pattern Phase 3 — fetch one workflow definition by stable id.</summary>
public record GetWorkflowDefinitionQuery(string DefinitionId)
    : IRequest<WorkflowDefinitionResponseModel>;

public class GetWorkflowDefinitionHandler(AppDbContext db)
    : IRequestHandler<GetWorkflowDefinitionQuery, WorkflowDefinitionResponseModel>
{
    public async Task<WorkflowDefinitionResponseModel> Handle(
        GetWorkflowDefinitionQuery request, CancellationToken ct)
    {
        var row = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DefinitionId == request.DefinitionId, ct)
            ?? throw new KeyNotFoundException($"Workflow definition '{request.DefinitionId}' not found.");

        return new WorkflowDefinitionResponseModel(
            row.Id, row.DefinitionId, row.EntityType, row.DefaultMode,
            row.StepsJson, row.ExpressTemplateComponent, row.IsSeedData);
    }
}
