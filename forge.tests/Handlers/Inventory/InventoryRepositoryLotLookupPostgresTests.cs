using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// Lot-aware bin content lookups against real Postgres, where a null lot compares through SQL rather than
/// C# equality: each lot (including no lot) finds only its own row, and the location list puts un-lotted
/// stock first, then the oldest lot.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class InventoryRepositoryLotLookupPostgresTests(PostgresFixture fixture)
{
    private async Task<(int PartId, int LocationId, int UnlottedId, int LotAId, int LotBId)> SeedAsync()
    {
        var partId = Random.Shared.Next(1_000_000, int.MaxValue);
        await using var seed = fixture.CreateContext();
        var bin = new StorageLocation
        {
            Name = $"LOT-PG-{Guid.NewGuid():N}"[..20],
            LocationType = LocationType.Bin,
            IsActive = true,
        };
        seed.StorageLocations.Add(bin);
        await seed.SaveChangesAsync();

        var placed = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var lotB = new BinContent { LocationId = bin.Id, EntityId = partId, Quantity = 24m, LotNumber = "HEAT-B", PlacedAt = placed.AddDays(2) };
        var lotA = new BinContent { LocationId = bin.Id, EntityId = partId, Quantity = 24m, LotNumber = "HEAT-A", PlacedAt = placed.AddDays(1) };
        var unlotted = new BinContent { LocationId = bin.Id, EntityId = partId, Quantity = 6m, PlacedAt = placed.AddDays(3) };
        var removed = new BinContent { LocationId = bin.Id, EntityId = partId, Quantity = 0m, PlacedAt = placed, RemovedAt = placed.AddDays(1) };
        seed.BinContents.AddRange(lotB, lotA, unlotted, removed);
        await seed.SaveChangesAsync();

        return (partId, bin.Id, unlotted.Id, lotA.Id, lotB.Id);
    }

    [Fact]
    public async Task FindActiveBinContentByPartLocationLotAsync_returnsOnlyTheRowForThatLot()
    {
        var (partId, locationId, unlottedId, lotAId, lotBId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        var repo = new InventoryRepository(db);

        (await repo.FindActiveBinContentByPartLocationLotAsync(partId, locationId, null, CancellationToken.None))!
            .Id.Should().Be(unlottedId);
        (await repo.FindActiveBinContentByPartLocationLotAsync(partId, locationId, "HEAT-A", CancellationToken.None))!
            .Id.Should().Be(lotAId);
        (await repo.FindActiveBinContentByPartLocationLotAsync(partId, locationId, "HEAT-B", CancellationToken.None))!
            .Id.Should().Be(lotBId);
        (await repo.FindActiveBinContentByPartLocationLotAsync(partId, locationId, "HEAT-C", CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetActiveBinContentsByPartLocationAsync_listsUnlottedFirstThenOldestLot()
    {
        var (partId, locationId, unlottedId, lotAId, lotBId) = await SeedAsync();
        await using var db = fixture.CreateContext();
        var repo = new InventoryRepository(db);

        var rows = await repo.GetActiveBinContentsByPartLocationAsync(partId, locationId, CancellationToken.None);

        rows.Select(r => r.Id).Should().Equal(unlottedId, lotAId, lotBId);
    }
}
