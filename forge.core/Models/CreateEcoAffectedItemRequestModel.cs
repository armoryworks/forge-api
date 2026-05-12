namespace Forge.Core.Models;

public record CreateEcoAffectedItemRequestModel
{
    public string EntityType { get; init; } = string.Empty;
    public int EntityId { get; init; }
    public string ChangeDescription { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}
