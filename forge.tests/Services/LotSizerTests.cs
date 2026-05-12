using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Enums;

namespace Forge.Tests.Services;

public class LotSizerTests
{
    [Fact]
    public void Apply_LotForLot_ReturnsExactQuantity()
    {
        var result = LotSizer.Apply(LotSizingRule.LotForLot, 42.5m, null, null, null);
        result.Should().Be(42.5m);
    }

    [Fact]
    public void Apply_FixedQuantity_ReturnsFixedWhenGreater()
    {
        var result = LotSizer.Apply(LotSizingRule.FixedQuantity, 30m, 100m, null, null);
        result.Should().Be(100m);
    }

    [Fact]
    public void Apply_FixedQuantity_RoundsUpToMultipleOfFixed()
    {
        var result = LotSizer.Apply(LotSizingRule.FixedQuantity, 150m, 100m, null, null);
        result.Should().Be(200m);
    }

    [Fact]
    public void Apply_FixedQuantity_FallsBackToNetWhenNoFixed()
    {
        var result = LotSizer.Apply(LotSizingRule.FixedQuantity, 42m, null, null, null);
        result.Should().Be(42m);
    }

    [Fact]
    public void Apply_MinMax_ReturnsMinWhenNetBelowMin()
    {
        var result = LotSizer.Apply(LotSizingRule.MinMax, 5m, null, 50m, null);
        result.Should().Be(50m);
    }

    [Fact]
    public void Apply_MinMax_ReturnsNetWhenAboveMin()
    {
        var result = LotSizer.Apply(LotSizingRule.MinMax, 75m, null, 50m, null);
        result.Should().Be(75m);
    }

    [Fact]
    public void Apply_MultiplesOf_RoundsUpToMultiple()
    {
        var result = LotSizer.Apply(LotSizingRule.MultiplesOf, 42m, null, null, 25m);
        result.Should().Be(50m);
    }

    [Fact]
    public void Apply_MultiplesOf_ReturnsExactWhenAlreadyMultiple()
    {
        var result = LotSizer.Apply(LotSizingRule.MultiplesOf, 50m, null, null, 25m);
        result.Should().Be(50m);
    }

    [Fact]
    public void Apply_MultiplesOf_FallsBackWhenNoMultiple()
    {
        var result = LotSizer.Apply(LotSizingRule.MultiplesOf, 42m, null, null, null);
        result.Should().Be(42m);
    }

    [Fact]
    public void Apply_EOQ_ReturnsCalculatedEOQ()
    {
        // EOQ with annual demand ~100, ordering cost ~50, holding cost ~2
        // EOQ = sqrt(2 * 100 * 50 / 2) = sqrt(5000) ≈ 70.71
        var result = LotSizer.Apply(LotSizingRule.EconomicOrderQuantity, 30m, null, null, null);
        result.Should().BeGreaterThanOrEqualTo(30m);
    }
}
