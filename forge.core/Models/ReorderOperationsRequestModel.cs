namespace Forge.Core.Models;

public record ReorderOperationsRequestModel(
    List<int> OperationIds, bool RemoveConflictingPredecessors = false, string? RoutingVersion = null);
