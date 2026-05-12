namespace Forge.Core.Models;

public record FmeaItemResponseModel
{
    public int Id { get; init; }
    public int ItemNumber { get; init; }
    public string? ProcessStep { get; init; }
    public string? Function { get; init; }
    public string FailureMode { get; init; } = string.Empty;
    public string PotentialEffect { get; init; } = string.Empty;
    public int Severity { get; init; }
    public string? Classification { get; init; }
    public string? PotentialCause { get; init; }
    public int Occurrence { get; init; }
    public string? CurrentPreventionControls { get; init; }
    public string? CurrentDetectionControls { get; init; }
    public int Detection { get; init; }
    public int Rpn { get; init; }
    public string? RecommendedAction { get; init; }
    public string? ResponsibleUserName { get; init; }
    public DateOnly? TargetCompletionDate { get; init; }
    public string? ActionTaken { get; init; }
    public DateTimeOffset? ActionCompletedAt { get; init; }
    public int? RevisedSeverity { get; init; }
    public int? RevisedOccurrence { get; init; }
    public int? RevisedDetection { get; init; }
    public int? RevisedRpn { get; init; }
    public int? CapaId { get; init; }
    public string? CapaCorrNum { get; init; }
}
