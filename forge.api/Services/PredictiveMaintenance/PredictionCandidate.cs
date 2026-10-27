using Forge.Core.Enums;

namespace Forge.Api.Services.PredictiveMaintenance;

/// <summary>One rule's finding for one asset before it is stored as a prediction: what it predicts, how sure the
/// rule tier is, when it expects the failure or service, and the numbers that made it fire.</summary>
public sealed record PredictionCandidate(
    int AssetId,
    string RuleKey,
    string PredictionType,
    decimal ConfidencePercent,
    MaintenancePredictionSeverity Severity,
    DateTimeOffset PredictedFailureDate,
    decimal? RemainingUsefulLifeHours,
    IReadOnlyDictionary<string, object?> Evidence);
