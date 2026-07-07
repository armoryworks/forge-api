using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Services;

/// <summary>
/// ai-fleet-orchestration D-crux. Sums per-capability model footprints, applies the customization
/// tier's overhead, adds headroom, and hints at topology (single box vs. offload).
/// <para>
/// <b>Sizing numbers grounded 2026-07-07</b> (research pass, Ollama Q4_K_M defaults — the community
/// standard quantization):
/// Small = 1–4B chat models (llama3.2:3b ≈ 2.0 GB disk; gemma3:4b ≈ 3.3 GB) + the all-minilm
/// embedder (46 MB); ~4 GB resident incl. KV cache/runtime.
/// Medium = 7–12B (8B ≈ 5 GB disk / 6–7 GB resident; gemma3:12b ≈ 8 GB disk / ~10 GB resident).
/// Large = 27–32B (gemma3:27b ≈ 17 GB disk / 16–24 GB resident; 32B ≈ 22–24 GB resident).
/// Deployment targets (decision 2026-07-07): Pi 5 8 GB runs ONE Small (Tier 0); a 32 GB mini-PC
/// (CPU-only) runs several Small/Medium; Large-class wants the 64 GB / consumer-GPU box.
/// Sources: ollama.com/library (llama3.2, gemma3, all-minilm tags);
/// localaimaster.com/blog/ollama-model-ram-vram-table; localllm.in/blog/ollama-vram-requirements-for-local-llms.
/// </para>
/// </summary>
public sealed class AiHardwareAdvisor : IAiHardwareAdvisor
{
    // Per-model-class resident footprint (MB) at Q4_K_M — grounded 2026-07-07 (see class doc for sources).
    // Ram = weights + KV cache + runtime overhead; Disk = model blob(s) incl. the shared embedder.
    private static (int Ram, int Disk) ClassFootprint(AiModelClass c) => c switch
    {
        AiModelClass.Small => (4_000, 3_500),
        AiModelClass.Medium => (9_000, 8_500),
        AiModelClass.Large => (22_000, 20_000),
        _ => (4_000, 3_500),
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
