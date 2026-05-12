using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateEcoRequestModel
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public EcoChangeType? ChangeType { get; init; }
    public EcoPriority? Priority { get; init; }
    public string? ReasonForChange { get; init; }
    public string? ImpactAnalysis { get; init; }
    public DateOnly? EffectiveDate { get; init; }
}
