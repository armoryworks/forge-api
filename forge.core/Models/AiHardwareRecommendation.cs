namespace Forge.Core.Models;

/// <summary>
/// ai-fleet-orchestration D-crux: recommended host resources for a set of enabled AI capabilities
/// at a chosen customization tier, plus a topology hint (single box vs. offload/distribute).
/// NB: the per-class sizing numbers are engineering estimates pending the model-sizing research pass.
/// </summary>
public record AiHardwareRecommendation(
    int RecommendedRamMb,
    int RecommendedDiskMb,
    string TopologyHint);
