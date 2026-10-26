using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Shipping;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipping;

/// <summary>
/// The wave builder writes real foreign keys (part, storage location, shipment line), which only a real
/// Postgres checks: a wave sourced from the oldest stocked bin saves, and a part with no stock lands on the
/// default location rather than a made-up id.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PickWavePostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Auto_generate_saves_a_wave_sourced_from_the_oldest_stocked_bin()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var due = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(Random.Shared.Next(1, 500_000));

        await using (var db = fixture.CreateContext())
        {
            var customer = new Customer { Name = $"Pick {tag}" };
            var stocked = new Part { PartNumber = $"PK-{tag}", Name = "Stocked", InventoryClass = InventoryClass.FinishedGood };
            var empty = new Part { PartNumber = $"PE-{tag}", Name = "Unstocked", InventoryClass = InventoryClass.FinishedGood };
            var oldBin = new StorageLocation { Name = $"OLD-{tag}", LocationType = LocationType.Bin, IsActive = true };
            var newBin = new StorageLocation { Name = $"NEW-{tag}", LocationType = LocationType.Bin, IsActive = true };
            db.AddRange(customer, stocked, empty, oldBin, newBin);
            await db.SaveChangesAsync();

            db.BinContents.AddRange(
                new BinContent { LocationId = newBin.Id, EntityType = "part", EntityId = stocked.Id, Quantity = 5m, Status = BinContentStatus.Stored, PlacedAt = due.AddDays(-1) },
                new BinContent { LocationId = oldBin.Id, EntityType = "part", EntityId = stocked.Id, Quantity = 5m, LotNumber = $"L-{tag}", Status = BinContentStatus.Stored, PlacedAt = due.AddDays(-9) });
            var order = new SalesOrder
            {
                OrderNumber = $"SO-{tag}", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed, RequestedDeliveryDate = due,
                Lines =
                [
                    new SalesOrderLine { PartId = stocked.Id, Description = "Stocked", Quantity = 2m, UnitPrice = 1m, LineNumber = 1 },
                    new SalesOrderLine { PartId = empty.Id, Description = "Unstocked", Quantity = 1m, UnitPrice = 1m, LineNumber = 2 },
                ],
            };
            db.Add(order);
            await db.SaveChangesAsync();

            db.Shipments.Add(new Shipment
            {
                ShipmentNumber = $"SH-{tag}", SalesOrderId = order.Id, Status = ShipmentStatus.Pending,
                Lines = order.Lines.Select(l => new ShipmentLine { SalesOrderLineId = l.Id, PartId = l.PartId, Quantity = l.Quantity }).ToList(),
            });
            await db.SaveChangesAsync();
        }

        PickWaveResponseModel wave;
        await using (var db = fixture.CreateContext())
        {
            wave = await new AutoGeneratePickWaveHandler(db).Handle(new AutoGeneratePickWaveCommand(new AutoWaveParametersModel
            {
                ShipByDateFrom = due, ShipByDateTo = due,
            }), CancellationToken.None);
        }

        await using (var db = fixture.CreateContext())
        {
            var detail = await new GetPickWaveHandler(db).Handle(new GetPickWaveQuery(wave.Id), CancellationToken.None);
            detail.TotalLines.Should().Be(2);
            var stockedLine = detail.Lines.Single(l => l.PartNumber == $"PK-{tag}");
            stockedLine.FromLocationName.Should().Be($"OLD-{tag}");
            stockedLine.LotNumber.Should().Be($"L-{tag}");
            var emptyLine = detail.Lines.Single(l => l.PartNumber == $"PE-{tag}");
            (await db.StorageLocations.AnyAsync(l => l.Name == emptyLine.FromLocationName && l.IsActive)).Should().BeTrue();
        }
    }
}
