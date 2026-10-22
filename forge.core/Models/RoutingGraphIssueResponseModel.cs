using Forge.Core.Enums;

namespace Forge.Core.Models;

public record RoutingGraphIssueResponseModel(
    RoutingGraphIssueKind Kind,
    int OperationId,
    int? StepNumber,
    int? PredecessorOperationId,
    int? PredecessorStepNumber);
