namespace Forge.Core.Models;

public record UpdateFmeaItemRequestModel
{
    public string? ProcessStep { get; init; }
    public string? Function { get; init; }
    public string? FailureMode { get; init; }
    public string? PotentialEffect { get; init; }
    public int? Severity { get; init; }
    public string? Classification { get; init; }
    public string? PotentialCause { get; init; }
    public int? Occurrence { get; init; }
    public string? CurrentPreventionControls { get; init; }
    public string? CurrentDetectionControls { get; init; }
    public int? Detection { get; init; }
    public string? RecommendedAction { get; init; }
    public int? ResponsibleUserId { get; init; }
    public DateOnly? TargetCompletionDate { get; init; }
}
