using FluentAssertions;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

/// <summary>
/// Integration-style test for <see cref="InventoryRepository.GetBinLocationsPagedAsync"/>
/// — verifies search filters across name / barcode / path and that paging
/// caps at 100 / floors at 1.
/// </summary>
public class InventoryRepositoryBinSearchTests
{
    private readonly Data.Context.AppDbContext _db;
    private readonly InventoryRepository _repo;

    public InventoryRepositoryBinSearchTests()
    {
        _db = TestDbContextFactory.Create();
        _repo = new InventoryRepository(_db);

        // Warehouse > Aisle 1 > A1, A2; Warehouse > Aisle 2 > B1
        var warehouse = new StorageLocation { Id = 1, Name = "Warehouse", LocationType = LocationType.Area };
        var aisle1 = new StorageLocation { Id = 2, Name = "Aisle 1", LocationType = LocationType.Rack, ParentId = 1 };
        var aisle2 = new StorageLocation { Id = 3, Name = "Aisle 2", LocationType = LocationType.Rack, ParentId = 1 };
        var binA1 = new StorageLocation { Id = 4, Name = "BIN-A1", LocationType = LocationType.Bin, ParentId = 2, Barcode = "BC-A1" };
        var binA2 = new StorageLocation { Id = 5, Name = "BIN-A2", LocationType = LocationType.Bin, ParentId = 2, Barcode = "BC-A2" };
        var binB1 = new StorageLocation { Id = 6, Name = "BIN-B1", LocationType = LocationType.Bin, ParentId = 3, Barcode = "BC-B1" };

        _db.StorageLocations.AddRange(warehouse, aisle1, aisle2, binA1, binA2, binB1);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_NoFilter_ReturnsAllBins()
    {
        var result = await _repo.GetBinLocationsPagedAsync(null, 1, 20, CancellationToken.None);

        result.Items.Should().HaveCount(3);
        result.Items.Select(b => b.Name).Should().BeEquivalentTo(new[] { "BIN-A1", "BIN-A2", "BIN-B1" });
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_SearchByName_FiltersBins()
    {
        var result = await _repo.GetBinLocationsPagedAsync("A1", 1, 20, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("BIN-A1");
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_SearchByBarcode_FiltersBins()
    {
        var result = await _repo.GetBinLocationsPagedAsync("BC-B1", 1, 20, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("BIN-B1");
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_SearchByPath_FiltersBins()
    {
        // "Aisle 2" appears only in BIN-B1's composed path
        var result = await _repo.GetBinLocationsPagedAsync("Aisle 2", 1, 20, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].Name.Should().Be("BIN-B1");
        result.Items[0].LocationPath.Should().Contain("Aisle 2");
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_PageSize_CapsAtHundred()
    {
        var result = await _repo.GetBinLocationsPagedAsync(null, 1, 5000, CancellationToken.None);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetBinLocationsPagedAsync_RespectsPagination()
    {
        var page1 = await _repo.GetBinLocationsPagedAsync(null, 1, 2, CancellationToken.None);
        var page2 = await _repo.GetBinLocationsPagedAsync(null, 2, 2, CancellationToken.None);

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(3);
        page2.TotalCount.Should().Be(3);
    }
}
