using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Shipments;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Concurrency;

/// <summary>
/// Shipping draws stock, so two ships racing must not both draw it. A second ship of the same shipment
/// waits for the first and then finds it already shipped; two shipments of one part never share the
/// same free units, so the bin is never drawn below zero.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ShipmentShipSerializationTests(PostgresFixture fixture)
{
    private static readonly TimeSpan BlockedFor = TimeSpan.FromMilliseconds(500);

    private static ShipShipmentHandler Handler(AppDbContext db) => new(
        new ShipmentRepository(db),
        new InventoryReliefService(db, NullLogger<InventoryReliefService>.Instance),
        db: db);

    private async Task<(int PartId, int BinId, List<int> ShipmentIds)> SeedAsync(decimal onHand, params decimal[] shipQuantities)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateContext();
        var customer = new Customer { Name = $"Ship race {tag}" };
        var part = new Part { PartNumber = $"SHIP-{tag}", Name = "Ship race part", InventoryClass = InventoryClass.FinishedGood };
        var location = new StorageLocation { Name = $"SR-{tag}", LocationType = LocationType.Bin, IsActive = true };
        db.AddRange(customer, part, location);
        await db.SaveChangesAsync();

        var bin = new BinContent
        {
            LocationId = location.Id,
            EntityType = "part",
            EntityId = part.Id,
            Quantity = onHand,
            Status = BinContentStatus.Stored,
            PlacedAt = DateTimeOffset.UtcNow,
        };
        var order = new SalesOrder
        {
            OrderNumber = $"SO-{tag}",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            Lines = [new SalesOrderLine { PartId = part.Id, Description = "Ship race part", Quantity = shipQuantities.Sum(), UnitPrice = 1m, LineNumber = 1 }],
        };
        db.AddRange(bin, order);
        await db.SaveChangesAsync();

        var shipments = shipQuantities.Select((qty, i) => new Shipment
        {
            ShipmentNumber = $"SH-{tag}-{i}",
            SalesOrderId = order.Id,
            Status = ShipmentStatus.Pending,
            Lines = [new ShipmentLine { SalesOrderLineId = order.Lines.First().Id, PartId = part.Id, Quantity = qty }],
        }).ToList();
        db.Shipments.AddRange(shipments);
        await db.SaveChangesAsync();
        return (part.Id, bin.Id, shipments.Select(s => s.Id).ToList());
    }

    [Fact]
    public async Task SecondShipOfTheSameShipment_WaitsForTheFirst_AndThenRefuses_DrawingOnce()
    {
        var (partId, binId, shipmentIds) = await SeedAsync(10m, 4m);
        var shipmentId = shipmentIds.Single();

        await using var holder = fixture.CreateContext();
        await using var held = await holder.Database.BeginTransactionAsync();
        await holder.AcquireAdvisoryLockAsync("shipments.ship", shipmentId.ToString(), CancellationToken.None);

        await using var db = fixture.CreateContext();
        var second = Handler(db).Handle(new ShipShipmentCommand(shipmentId), CancellationToken.None);
        (await Task.WhenAny(second, Task.Delay(BlockedFor))).Should().NotBe(second);

        await Handler(holder).Handle(new ShipShipmentCommand(shipmentId), CancellationToken.None);
        await held.CommitAsync();

        var act = () => second;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only Pending or Packed shipments can be shipped");

        await using var verify = fixture.CreateContext();
        (await verify.BinContents.SingleAsync(b => b.Id == binId)).Quantity.Should().Be(6m);
        (await verify.BinMovements.CountAsync(m => m.EntityType == "part" && m.EntityId == partId && m.Reason == BinMovementReason.Ship))
            .Should().Be(1);
    }

    [Fact]
    public async Task TwoShipmentsOfOnePart_InParallel_ShipOneAndRefuseTheOther_NeverNegative()
    {
        var (_, binId, shipmentIds) = await SeedAsync(5m, 5m, 5m);

        var outcomes = await Task.WhenAll(shipmentIds.Select(async id =>
        {
            await using var db = fixture.CreateContext();
            try
            {
                await Handler(db).Handle(new ShipShipmentCommand(id), CancellationToken.None);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }));

        outcomes.Count(e => e is null).Should().Be(1);
        outcomes.Single(e => e is not null).Should().BeOfType<InsufficientStockException>();

        await using var verify = fixture.CreateContext();
        (await verify.BinContents.SingleAsync(b => b.Id == binId)).Quantity.Should().Be(0m);
        (await verify.Shipments.CountAsync(s => shipmentIds.Contains(s.Id) && s.Status == ShipmentStatus.Shipped))
            .Should().Be(1);
    }
}
