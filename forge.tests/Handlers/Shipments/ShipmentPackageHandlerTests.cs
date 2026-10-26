using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public sealed class ShipmentPackageHandlerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Shipment> SeedAsync()
    {
        var customer = new Customer { Name = "Buyer Co" };
        _db.Add(customer);
        await _db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-1", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.Add(order);
        await _db.SaveChangesAsync();
        var shipment = new Shipment { ShipmentNumber = "SH-1", SalesOrderId = order.Id, Status = ShipmentStatus.Pending };
        _db.Add(shipment);
        await _db.SaveChangesAsync();
        return shipment;
    }

    [Fact]
    public async Task Adding_updating_and_removing_a_package_keeps_its_dimensions_and_logs_on_the_shipment()
    {
        var shipment = await SeedAsync();

        var added = await new AddShipmentPackageHandler(_db).Handle(
            new AddShipmentPackageCommand(shipment.Id, null, null, 12.5m, 10m, 8m, 6m), CancellationToken.None);
        var updated = await new UpdateShipmentPackageHandler(_db).Handle(
            new UpdateShipmentPackageCommand(shipment.Id, added.Id, "1Z999", null, 13m, null, 12m, 9m, 7m), CancellationToken.None);

        updated.Weight.Should().Be(13m);
        updated.Length.Should().Be(12m);
        updated.Width.Should().Be(9m);
        updated.Height.Should().Be(7m);
        updated.TrackingNumber.Should().Be("1Z999");

        await new RemoveShipmentPackageHandler(_db).Handle(new RemoveShipmentPackageCommand(shipment.Id, added.Id), CancellationToken.None);

        (await _db.ShipmentPackages.AnyAsync()).Should().BeFalse();
        (await _db.ActivityLogs.Where(a => a.EntityType == "Shipment" && a.EntityId == shipment.Id).Select(a => a.Action).ToListAsync())
            .Should().Contain(["package-added", "package-updated", "package-removed"]);
    }

    [Fact]
    public void Negative_dimensions_fail_validation()
    {
        new UpdateShipmentPackageValidator()
            .Validate(new UpdateShipmentPackageCommand(1, 1, null, null, -1m, null, Length: -2m))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Weight", "Length"]);
    }
}
