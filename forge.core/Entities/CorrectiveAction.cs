using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class CorrectiveAction : BaseAuditableEntity
{
    public string CapaNumber { get; set; } = string.Empty;
    public CapaType Type { get; set; }
    public CapaSourceType SourceType { get; set; }
    public int? SourceEntityId { get; set; }
    public string? SourceEntityType { get; set; }

    // Problem definition
    public string Title { get; set; } = string.Empty;
    public string ProblemDescription { get; set; } = string.Empty;
    public string? ImpactDescription { get; set; }

    // Root cause analysis
    public string? RootCauseAnalysis { get; set; }
    public RootCauseMethod? RootCauseMethod { get; set; }
    public string? RootCauseMethodData { get; set; }
    public int? RootCauseAnalyzedById { get; set; }
    public DateTimeOffset? RootCauseCompletedAt { get; set; }

    // Actions
    public string? ContainmentAction { get; set; }
    public string? CorrectiveActionDescription { get; set; }
    public string? PreventiveAction { get; set; }

    // Verification
    public string? VerificationMethod { get; set; }
    public string? VerificationResult { get; set; }
    public int? VerifiedById { get; set; }
    public DateTimeOffset? VerificationDate { get; set; }

    // Effectiveness check
    public DateTimeOffset? EffectivenessCheckDueDate { get; set; }
    public DateTimeOffset? EffectivenessCheckDate { get; set; }
    public string? EffectivenessResult { get; set; }
    public bool? IsEffective { get; set; }
    public int? EffectivenessCheckedById { get; set; }

    // Ownership & status
    public int OwnerId { get; set; }
    public CapaStatus Status { get; set; } = CapaStatus.Open;
    public int Priority { get; set; } = 3;
    public DateTimeOffset DueDate { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int? ClosedById { get; set; }

    // Navigation (no ApplicationUser nav properties — FK-only pattern)
    public ICollection<CapaTask> Tasks { get; set; } = [];
    public ICollection<NonConformance> RelatedNcrs { get; set; } = [];
}
