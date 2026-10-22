namespace Forge.Core.Models;

public record UpdateRoutingGraphRequestModel(string? Version, List<RoutingGraphStepRequestModel> Steps);
