using FluentAssertions;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Inventory;

public class InventoryRepositoryMovementsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetMovementsAsync_SignsEachRowByItsDirection()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.Add(new Part { Id = 1, PartNumber = "P-1", Name = "Bracket" });
        db.BinMovements.AddRange(
            new BinMovement { Id = 1, EntityId = 1, Quantity = 4m, FromLocationId = 10, Reason = BinMovementReason.Adjustment, MovedAt = At.AddMinutes(1) },
            new BinMovement { Id = 2, EntityId = 1, Quantity = 6m, ToLocationId = 10, Reason = BinMovementReason.Adjustment, MovedAt = At.AddMinutes(2) },
            new BinMovement { Id = 3, EntityId = 1, Quantity = 3m, FromLocationId = 10, ToLocationId = 11, Reason = BinMovementReason.Transfer, MovedAt = At.AddMinutes(3) },
            new BinMovement { Id = 4, EntityType = "ShipmentLine", EntityId = 50, Quantity = -3000m, FromLocationId = 10, Reason = BinMovementReason.Ship, MovedAt = At.AddMinutes(4) });
        await db.SaveChangesAsync();

        var rows = await new InventoryRepository(db).GetMovementsAsync(null, null, null, 50, CancellationToken.None);

        rows.Single(r => r.Id == 1).Quantity.Should().Be(-4m);
        rows.Single(r => r.Id == 2).Quantity.Should().Be(6m);
        rows.Single(r => r.Id == 3).Quantity.Should().Be(3m);
        rows.Single(r => r.Id == 4).Quantity.Should().Be(-3000m);
    }

    [Fact]
    public async Task GetMovementsAsync_ARowThatTouchesNoBinKeepsItsStoredSign()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.Add(new Part { Id = 1, PartNumber = "P-1", Name = "Bracket" });
        db.BinMovements.Add(new BinMovement { Id = 1, EntityId = 1, Quantity = -20m, Reason = BinMovementReason.Adjustment, MovedAt = At });
        await db.SaveChangesAsync();

        var rows = await new InventoryRepository(db).GetMovementsAsync(null, null, null, 50, CancellationToken.None);

        rows.Single().Quantity.Should().Be(-20m);
    }

    [Fact]
    public async Task GetMovementsAsync_APartsHistoryIncludesItsShipments()
    {
        using var db = TestDbContextFactory.Create();
        db.Parts.AddRange(
            new Part { Id = 1, PartNumber = "P-1", Name = "Bracket" },
            new Part { Id = 2, PartNumber = "P-2", Name = "Hinge" });
        db.SalesOrderLines.Add(new SalesOrderLine { Id = 70, SalesOrderId = 1, PartId = 1, Description = "Bracket" });
        db.ShipmentLines.AddRange(
            new ShipmentLine { Id = 50, ShipmentId = 1, PartId = 1, Quantity = 5m },
            new ShipmentLine { Id = 51, ShipmentId = 1, SalesOrderLineId = 70, Quantity = 2m },
            new ShipmentLine { Id = 52, ShipmentId = 1, PartId = 2, Quantity = 1m });
        db.BinMovements.AddRange(
            new BinMovement { Id = 1, EntityId = 1, Quantity = 9m, ToLocationId = 10, MovedAt = At },
            new BinMovement { Id = 2, EntityType = "ShipmentLine", EntityId = 50, Quantity = -5m, FromLocationId = 10, MovedAt = At.AddMinutes(1) },
            new BinMovement { Id = 3, EntityType = "ShipmentLine", EntityId = 51, Quantity = -2m, FromLocationId = 10, MovedAt = At.AddMinutes(2) },
            new BinMovement { Id = 4, EntityType = "ShipmentLine", EntityId = 52, Quantity = -1m, FromLocationId = 10, MovedAt = At.AddMinutes(3) },
            new BinMovement { Id = 5, EntityId = 2, Quantity = 4m, ToLocationId = 10, MovedAt = At.AddMinutes(4) });
        await db.SaveChangesAsync();

        var rows = await new InventoryRepository(db).GetMovementsAsync(null, "part", 1, 50, CancellationToken.None);

        rows.Select(r => r.Id).Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public async Task GetMovementsAsync_NamesShipmentRowsByTheShippedPart()
    {
        using var db = TestDbContextFactory.Create();
        db.UnitsOfMeasure.Add(new UnitOfMeasure { Id = 3, Code = "EA", Name = "Each" });
        db.Parts.AddRange(
            new Part { Id = 1, PartNumber = "P-1", Name = "Bracket", Description = "Steel bracket", StockUomId = 3 },
            new Part { Id = 2, PartNumber = "P-2", Name = "Hinge" });
        db.SalesOrderLines.Add(new SalesOrderLine { Id = 70, SalesOrderId = 1, PartId = 2, Description = "Hinge" });
        db.ShipmentLines.AddRange(
            new ShipmentLine { Id = 50, ShipmentId = 1, PartId = 1, Quantity = 5m },
            new ShipmentLine { Id = 51, ShipmentId = 1, SalesOrderLineId = 70, Quantity = 2m });
        db.BinMovements.AddRange(
            new BinMovement { Id = 1, EntityType = "ShipmentLine", EntityId = 50, Quantity = -5m, FromLocationId = 10, MovedAt = At },
            new BinMovement { Id = 2, EntityType = "ShipmentLine", EntityId = 51, Quantity = -2m, FromLocationId = 10, MovedAt = At.AddMinutes(1) },
            new BinMovement { Id = 3, EntityType = "ShipmentLine", EntityId = 99, Quantity = -1m, FromLocationId = 10, MovedAt = At.AddMinutes(2) });
        await db.SaveChangesAsync();

        var rows = await new InventoryRepository(db).GetMovementsAsync(null, null, null, 50, CancellationToken.None);

        var direct = rows.Single(r => r.Id == 1);
        direct.EntityName.Should().Be("P-1 — Steel bracket");
        direct.UomCode.Should().Be("EA");
        rows.Single(r => r.Id == 2).EntityName.Should().Be("P-2 — Hinge");
        rows.Single(r => r.Id == 3).EntityName.Should().Be("ShipmentLine:99");
    }
}
