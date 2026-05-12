namespace Forge.Core.Entities;

public class FmeaItem : BaseEntity
{
    public int FmeaId { get; set; }
    public int ItemNumber { get; set; }
    public string? ProcessStep { get; set; }
    public string? Function { get; set; }
    public string FailureMode { get; set; } = string.Empty;
    public string PotentialEffect { get; set; } = string.Empty;
    public int Severity { get; set; }
    public string? Classification { get; set; }
    public string? PotentialCause { get; set; }
    public int Occurrence { get; set; }
    public string? CurrentPreventionControls { get; set; }
    public string? CurrentDetectionControls { get; set; }
    public int Detection { get; set; }
    public string? RecommendedAction { get; set; }
    public int? ResponsibleUserId { get; set; }
    public DateOnly? TargetCompletionDate { get; set; }
    public string? ActionTaken { get; set; }
    public DateTimeOffset? ActionCompletedAt { get; set; }
    public int? RevisedSeverity { get; set; }
    public int? RevisedOccurrence { get; set; }
    public int? RevisedDetection { get; set; }
    public int? CapaId { get; set; }

    // Navigation (FK-only for ApplicationUser)
    public FmeaAnalysis Fmea { get; set; } = null!;
    public CorrectiveAction? Capa { get; set; }
}
