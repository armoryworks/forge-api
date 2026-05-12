namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — The recommendation engine's output for a given answer
/// set. Captures the recommended preset, alternatives (when confidence is
/// low), the rationale string, and the per-question factors that drove the
/// decision. The capability deltas relative to the current install state
/// are computed by the handler that wraps this engine — the engine itself
/// is stateless (per 4F Phase-F decisions D10).
/// </summary>
public record DiscoveryRecommendation(
    string PresetId,
    double Confidence,
    string ConfidenceLabel,
    string Rationale,
    IReadOnlyList<DiscoveryRecommendationFactor> Factors,
    IReadOnlyList<DiscoveryAlternative> Alternatives);

/// <summary>One factor (rationale point) with the question that drove it.</summary>
public record DiscoveryRecommendationFactor(string QuestionId, string Description);

/// <summary>Alternative preset surfaced when confidence is low.</summary>
public record DiscoveryAlternative(
    string PresetId,
    string PresetName,
    string DistinguishingRationale);
