using FluentAssertions;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Persistence;

/// <summary>
/// Verifies AppDbContext normalizes DateTimeOffset values carrying a non-zero offset to the
/// same instant at offset zero before save, since Npgsql rejects non-UTC offsets for
/// <c>timestamptz</c>. A carrier echoing a local ready time (e.g. -06:00) into
/// <see cref="Shipment.PickupScheduledDate"/> is the motivating case.
/// </summary>
public class DateTimeOffsetNormalizationTests
{
    [Fact]
    public async Task Save_NonZeroOffset_IsConvertedToUtcSameInstant()
    {
        await using var db = TestDbContextFactory.Create();
        var local = new DateTimeOffset(2026, 11, 20, 9, 30, 0, TimeSpan.FromHours(-6));
        var shipment = new Shipment { ShipmentNumber = "SHP-TZ-1", SalesOrderId = 1, PickupScheduledDate = local };
        db.Shipments.Add(shipment);

        await db.SaveChangesAsync();

        shipment.PickupScheduledDate!.Value.Offset.Should().Be(TimeSpan.Zero);
        shipment.PickupScheduledDate.Value.UtcDateTime.Should().Be(new DateTime(2026, 11, 20, 15, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Save_ModifiedEntity_NonZeroOffset_IsConvertedToUtc()
    {
        await using var db = TestDbContextFactory.Create();
        var shipment = new Shipment { ShipmentNumber = "SHP-TZ-2", SalesOrderId = 1 };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        shipment.ShippedDate = new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.FromHours(2));
        await db.SaveChangesAsync();

        shipment.ShippedDate!.Value.Offset.Should().Be(TimeSpan.Zero);
        shipment.ShippedDate.Value.UtcDateTime.Should().Be(new DateTime(2026, 11, 20, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Save_UtcOffset_IsLeftUnchanged()
    {
        await using var db = TestDbContextFactory.Create();
        var utc = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var shipment = new Shipment { ShipmentNumber = "SHP-TZ-3", SalesOrderId = 1, EstimatedDeliveryDate = utc };
        db.Shipments.Add(shipment);

        await db.SaveChangesAsync();

        shipment.EstimatedDeliveryDate.Should().Be(utc);
        shipment.EstimatedDeliveryDate!.Value.Offset.Should().Be(TimeSpan.Zero);
    }
}
