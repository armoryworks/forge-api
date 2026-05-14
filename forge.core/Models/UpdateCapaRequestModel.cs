
namespace Forge.Core.Models;

public record UpdateCapaRequestModel
{
    public string? Title { get; init; }
    public string? ProblemDescription { get; init; }
    public string? ImpactDescription { get; init; }
    public string? RootCauseAnalysis { get; init; }
    public RootCauseMethod? RootCauseMethod { get; init; }
    public string? RootCauseMethodData { get; init; }
    public string? ContainmentAction { get; init; }
    public string? CorrectiveActionDescription { get; init; }
    public string? PreventiveAction { get; init; }
    public string? VerificationMethod { get; init; }
    public string? VerificationResult { get; init; }
    public string? EffectivenessResult { get; init; }
    public bool? IsEffective { get; init; }
    public int? OwnerId { get; init; }
    public int? Priority { get; init; }
    public DateTimeOffset? DueDate { get; init; }
}
