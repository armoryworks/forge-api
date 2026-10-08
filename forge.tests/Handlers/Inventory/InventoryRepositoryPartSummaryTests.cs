using FluentAssertions;
using Forge.Core.Entities;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class InventoryRepositoryPartSummaryTests
{
    [Fact]
    public async Task GetPartInventorySummaryAsync_CarriesEachPartsStockLevels()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.AddRange(
            new Part { Id = 1, PartNumber = "P-1", Name = "Tracked", MinStockThreshold = 10m, ReorderPoint = 15m },
            new Part { Id = 2, PartNumber = "P-2", Name = "Untracked" });
        await db.SaveChangesAsync();

        var result = await new InventoryRepository(db).GetPartInventorySummaryAsync(null, CancellationToken.None);

        var tracked = result.Single(p => p.PartId == 1);
        tracked.MinStockThreshold.Should().Be(10m);
        tracked.ReorderPoint.Should().Be(15m);

        var untracked = result.Single(p => p.PartId == 2);
        untracked.MinStockThreshold.Should().BeNull();
        untracked.ReorderPoint.Should().BeNull();
    }
}
