using FluentAssertions;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class GetPartInventorySummaryTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_ReturnsBinContentIdLotAndStatusPerBin()
    {
        var part = new Part { PartNumber = "BAR-12", Description = "12 ft bar" };
        var otherPart = new Part { PartNumber = "BAR-6", Description = "6 ft bar" };
        _db.Parts.AddRange(part, otherPart);
        var area = new StorageLocation { Name = "Main", LocationType = LocationType.Area };
        _db.StorageLocations.Add(area);
        await _db.SaveChangesAsync();
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, ParentId = area.Id };
        _db.StorageLocations.Add(bin);
        await _db.SaveChangesAsync();

        var lotted = new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 10m, ReservedQuantity = 4m,
            LotNumber = "HEAT-A", Status = BinContentStatus.Reserved,
        };
        var held = new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 3m, Status = BinContentStatus.QcHold,
        };
        var removed = new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 99m,
            RemovedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var otherPartStock = new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = otherPart.Id, Quantity = 50m,
        };
        _db.BinContents.AddRange(lotted, held, removed, otherPartStock);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await new GetPartInventorySummaryHandler(_db)
            .Handle(new GetPartInventorySummaryQuery(part.Id), CancellationToken.None);

        result.TotalQuantity.Should().Be(13m);
        result.ReservedQuantity.Should().Be(4m);
        result.AvailableQuantity.Should().Be(9m);
        result.BinLocations.Should().HaveCount(2);

        var lottedRow = result.BinLocations.Single(b => b.BinContentId == lotted.Id);
        lottedRow.LocationPath.Should().Be("Main / A1");
        lottedRow.LotNumber.Should().Be("HEAT-A");
        lottedRow.Status.Should().Be(BinContentStatus.Reserved);
        lottedRow.AvailableQuantity.Should().Be(6m);

        var heldRow = result.BinLocations.Single(b => b.BinContentId == held.Id);
        heldRow.LotNumber.Should().BeNull();
        heldRow.Status.Should().Be(BinContentStatus.QcHold);
        heldRow.AvailableQuantity.Should().Be(3m);
    }

    [Fact]
    public async Task Handle_UnknownPart_Throws()
    {
        var act = () => new GetPartInventorySummaryHandler(_db)
            .Handle(new GetPartInventorySummaryQuery(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
