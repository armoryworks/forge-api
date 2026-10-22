using Forge.Core.Entities;

namespace Forge.Core.Routing;

/// <summary>
/// A progress update to check against step gating. <paramref name="Completed"/> and <paramref name="Scrap"/> are
/// the step's totals after the update; <paramref name="PreviousCompleted"/> and <paramref name="PreviousScrap"/>
/// before it.
/// </summary>
public sealed record StepGateRequest(
    Job Job,
    Operation Operation,
    decimal JobQuantity,
    decimal PreviousCompleted,
    decimal PreviousScrap,
    decimal Completed,
    decimal Scrap,
    bool MarkingComplete,
    bool Acknowledge,
    string? Reason,
    bool UserMayOverride);
