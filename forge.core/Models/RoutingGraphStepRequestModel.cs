namespace Forge.Core.Models;

public record RoutingGraphStepRequestModel(int OperationId, int StepNumber, List<OperationPredecessorRequestModel>? Predecessors);
