using Forge.Core.Enums;

namespace Forge.Core.Models;

public record CapaResponseModel
{
    public int Id { get; init; }
    public string CapaNumber { get; init; } = string.Empty;
    public CapaType Type { get; init; }
    public CapaSourceType SourceType { get; init; }
    public int? SourceEntityId { get; init; }
    public string? SourceEntityType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProblemDescription { get; init; } = string.Empty;
    public string? ImpactDescription { get; init; }
    public string? RootCauseAnalysis { get; init; }
    public RootCauseMethod? RootCauseMethod { get; init; }
    public string? RootCauseMethodData { get; init; }
    public int? RootCauseAnalyzedById { get; init; }
    public string? RootCauseAnalyzedByName { get; init; }
    public DateTimeOffset? RootCauseCompletedAt { get; init; }
    public string? ContainmentAction { get; init; }
    public string? CorrectiveActionDescription { get; init; }
    public string? PreventiveAction { get; init; }
    public string? VerificationMethod { get; init; }
    public string? VerificationResult { get; init; }
    public int? VerifiedById { get; init; }
    public string? VerifiedByName { get; init; }
    public DateTimeOffset? VerificationDate { get; init; }
    public DateTimeOffset? EffectivenessCheckDueDate { get; init; }
    public DateTimeOffset? EffectivenessCheckDate { get; init; }
    public string? EffectivenessResult { get; init; }
    public bool? IsEffective { get; init; }
    public int? EffectivenessCheckedById { get; init; }
    public string? EffectivenessCheckedByName { get; init; }
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public CapaStatus Status { get; init; }
    public int Priority { get; init; }
    public DateTimeOffset DueDate { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public int? ClosedById { get; init; }
    public string? ClosedByName { get; init; }
    public int TaskCount { get; init; }
    public int CompletedTaskCount { get; init; }
    public int RelatedNcrCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
