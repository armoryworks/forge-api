
namespace Forge.Core.Models;

public record CreateEcoRequestModel
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public EcoChangeType ChangeType { get; init; }
    public EcoPriority Priority { get; init; } = EcoPriority.Normal;
    public string? ReasonForChange { get; init; }
    public string? ImpactAnalysis { get; init; }
    public DateOnly? EffectiveDate { get; init; }
}
