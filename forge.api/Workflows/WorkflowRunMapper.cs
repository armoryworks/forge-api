using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Workflows;

internal static class WorkflowRunMapper
{
    public static WorkflowRunResponseModel ToResponse(this WorkflowRun row) => new(
        row.Id,
        row.EntityType,
        row.EntityId,
        row.DefinitionId,
        row.CurrentStepId,
        row.Mode,
        row.StartedAt,
        row.StartedByUserId,
        row.CompletedAt,
        row.AbandonedAt,
        row.AbandonedReason,
        row.LastActivityAt,
        row.Version);
}
