namespace Forge.Core.Models;

public record CpqRoutingPreview
{
    public string OperationName { get; init; } = "";
    public decimal EstimatedMinutes { get; init; }
}
