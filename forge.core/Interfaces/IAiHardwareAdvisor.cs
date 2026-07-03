using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// ai-fleet-orchestration D-crux: the infra-awareness advisor. Given the enabled AI capabilities
/// and the chosen customization tier, recommends host RAM/disk and whether to distribute across
/// boxes — the "hardware matrix run in reverse".
/// </summary>
public interface IAiHardwareAdvisor
{
    AiHardwareRecommendation Recommend(IReadOnlyList<AiCapabilityFootprint> enabled, AiCustomizationTier tier);
}
