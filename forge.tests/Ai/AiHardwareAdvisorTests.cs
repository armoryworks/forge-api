using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Ai;

/// <summary>
/// ai-fleet-orchestration D-crux. The hardware advisor fits small setups on one box and recommends
/// distribution once the summed footprint (× tier overhead) exceeds the single-box threshold.
/// Pure logic — no DB/AI.
/// </summary>
public sealed class AiHardwareAdvisorTests
{
    [Fact]
    public void Single_small_scaffold_fits_one_box()
    {
        var rec = new AiHardwareAdvisor().Recommend([new("smart-search", AiModelClass.Small)], AiCustomizationTier.Scaffold);

        rec.TopologyHint.Should().Contain("single box");
        rec.RecommendedRamMb.Should().BeGreaterThan(0);
        rec.RecommendedDiskMb.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Many_large_finetune_recommends_distribution()
    {
        var caps = new List<AiCapabilityFootprint>
        {
            new("a", AiModelClass.Large),
            new("b", AiModelClass.Large),
            new("c", AiModelClass.Large),
        };

        var rec = new AiHardwareAdvisor().Recommend(caps, AiCustomizationTier.FullFineTune);

        rec.TopologyHint.Should().Contain("Distribute");
        rec.RecommendedRamMb.Should().BeGreaterThan(24_000);
    }
}
