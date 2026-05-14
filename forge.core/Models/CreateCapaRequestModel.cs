
namespace Forge.Core.Models;

public record CreateCapaRequestModel
{
    public CapaType Type { get; init; }
    public CapaSourceType SourceType { get; init; }
    public int? SourceEntityId { get; init; }
    public string? SourceEntityType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProblemDescription { get; init; } = string.Empty;
    public string? ImpactDescription { get; init; }
    public int OwnerId { get; init; }
    public int Priority { get; init; } = 3;
    public DateTimeOffset DueDate { get; init; }
}
