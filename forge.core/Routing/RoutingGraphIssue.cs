using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>
/// One reason a whole-routing save was refused. <see cref="PredecessorOperationId"/> is set for the link
/// problems (self-link, unknown predecessor, predecessor not earlier); the step numbers are the requested ones.
/// </summary>
public sealed record RoutingGraphIssue(
    RoutingGraphIssueKind Kind,
    int OperationId,
    int? StepNumber,
    int? PredecessorOperationId = null,
    int? PredecessorStepNumber = null);
