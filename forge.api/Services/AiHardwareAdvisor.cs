using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// ai-fleet-orchestration D-crux. Sums per-capability model footprints, applies the customization
/// tier's overhead, adds headroom, and hints at topology (single box vs. offload). Sizing numbers
/// are engineering estimates pending the model-sizing research pass (Ollama/quantization tiers).
/// </summary>
public sealed class AiHardwareAdvisor : IAiHardwareAdvisor
{
    // Per-model-class resident footprint (MB) — rough estimates.
    private static (int Ram, int Disk) ClassFootprint(AiModelClass c) => c switch
    {
        AiModelClass.Small => (1_500, 2_000),
        AiModelClass.Medium => (5_000, 6_000),
        AiModelClass.Large => (16_000, 20_000),
        _ => (1_500, 2_000),
    };

    // Customization tier multiplies RAM overhead (adapters/fine-tune add resident weight).
    private static double TierRamFactor(AiCustomizationTier t) => t switch
    {
        AiCustomizationTier.Scaffold => 1.0,
        AiCustomizationTier.LoraAdapter => 1.2,
        AiCustomizationTier.FullFineTune => 1.5,
        _ => 1.0,
    };

    // Above this total RAM, recommend spreading capabilities across boxes.
    private const int DistributeThresholdMb = 24_000;

    public AiHardwareRecommendation Recommend(IReadOnlyList<AiCapabilityFootprint> enabled, AiCustomizationTier tier)
    {
        var ram = 0;
        var disk = 0;
        foreach (var cap in enabled)
        {
            var (capRam, capDisk) = ClassFootprint(cap.ModelClass);
            ram += capRam;
            disk += capDisk;
        }

        ram = (int)(ram * TierRamFactor(tier));

        // 30% RAM headroom; a small OS/runtime disk floor.
        var recommendedRam = (int)(ram * 1.3) + 2_000;
        var recommendedDisk = disk + 10_000;

        var hint = recommendedRam > DistributeThresholdMb
            ? "Distribute capabilities across multiple low-cost boxes (mini-PC/Pi per model); store models redundantly for failover."
            : "Fits a single box.";

        return new AiHardwareRecommendation(recommendedRam, recommendedDisk, hint);
    }
}
