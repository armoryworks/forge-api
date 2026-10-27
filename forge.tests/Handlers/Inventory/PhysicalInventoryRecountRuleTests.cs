using FluentAssertions;

using Forge.Api.Features.Inventory.PhysicalInventories;

namespace Forge.Tests.Handlers.Inventory;

public class PhysicalInventoryRecountRuleTests
{
    [Theory]
    [InlineData(100, 90, 0, false)]
    [InlineData(100, 90, 5, true)]
    [InlineData(100, 96, 5, false)]
    [InlineData(0, 3, 5, true)]
    [InlineData(100, 100, 5, false)]
    public void Recount_follows_the_tolerance(decimal frozen, decimal counted, decimal tolerance, bool expected)
    {
        PhysicalInventoryService.NeedsRecount(frozen, counted, tolerance)
            .Should().Be(expected);
    }
}
