using System.Text;

using FluentAssertions;
using Moq;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class ShipmentHazmatDocumentTests : IDisposable
{
    static ShipmentHazmatDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Class_comes_from_the_order_line_part_then_the_shipment_line_part()
    {
        var solvent = new Part { PartNumber = "CN-IPA", HazmatClass = " 3 " };
        var bracket = new Part { PartNumber = "FG-1" };

        ShipmentLineHazmat.ClassFor(new ShipmentLine { SalesOrderLine = new SalesOrderLine { Part = solvent }, Part = bracket })
            .Should().Be("3");
        ShipmentLineHazmat.ClassFor(new ShipmentLine { Part = solvent }).Should().Be("3");
        ShipmentLineHazmat.ClassFor(new ShipmentLine { Part = bracket }).Should().BeNull();
        ShipmentLineHazmat.ClassFor(new ShipmentLine { Part = new Part { HazmatClass = "  " } }).Should().BeNull();
    }

    [Fact]
    public void Any_hazmat_is_false_for_a_shipment_with_no_regulated_parts()
    {
        ShipmentLineHazmat.AnyHazmat([new ShipmentLine { Part = new Part() }]).Should().BeFalse();
        ShipmentLineHazmat.AnyHazmat([new ShipmentLine { Part = new Part() }, new ShipmentLine { Part = new Part { HazmatClass = "8" } }])
            .Should().BeTrue();
    }

    [Fact]
    public async Task Packing_slip_and_bill_of_lading_render_a_shipment_of_a_hazmat_part_without_an_order_line()
    {
        var customer = new Customer { Name = "Acme" };
        var solvent = new Part { PartNumber = "CN-IPA", Description = "Isopropyl alcohol", HazmatClass = "3" };
        _db.AddRange(customer, solvent);
        await _db.SaveChangesAsync();
        var order = new SalesOrder { OrderNumber = "SO-0001", CustomerId = customer.Id };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        var shipment = new Shipment
        {
            ShipmentNumber = "SH-0001", SalesOrderId = order.Id,
            Lines = [new ShipmentLine { PartId = solvent.Id, Quantity = 4 }],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var settings = new Mock<ISystemSettingRepository>();

        var slip = await new GeneratePackingSlipPdfHandler(_db, settings.Object)
            .Handle(new GeneratePackingSlipPdfQuery(shipment.Id), CancellationToken.None);
        var bol = await new GenerateBillOfLadingPdfHandler(_db, settings.Object)
            .Handle(new GenerateBillOfLadingPdfQuery(shipment.Id), CancellationToken.None);

        Encoding.ASCII.GetString(slip, 0, 4).Should().Be("%PDF");
        Encoding.ASCII.GetString(bol, 0, 4).Should().Be("%PDF");
    }
}
