using FluentAssertions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

using Forge.Api.Features.Shipments;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class CrossBorderShipmentDocumentTests : IDisposable
{
    static CrossBorderShipmentDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly ISystemSettingRepository _settings = Mock.Of<ISystemSettingRepository>();

    private async Task<int> SeedShipmentAsync(string shipToCountry)
    {
        _db.CompanyLocations.Add(new CompanyLocation
        {
            Name = "Plant", Line1 = "1 Main", City = "Provo", State = "UT", PostalCode = "84601", Country = "US", IsDefault = true,
        });
        var customer = new Customer { Name = "Export buyer" };
        _db.Customers.Add(customer);
        var made = new Part { PartNumber = "SP-KIT-01", Name = "Valve Service Kit", Description = "Valve Service Kit", HtsCode = "8481.90.9040", ProcurementSource = ProcurementSource.Make };
        var bought = new Part { PartNumber = "GSK-200", Name = "Gasket", Description = "Gasket", ProcurementSource = ProcurementSource.Buy };
        _db.Parts.AddRange(made, bought);
        var vendor = new Vendor { CompanyName = "Overseas Gaskets" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        _db.VendorParts.Add(new VendorPart { VendorId = vendor.Id, PartId = bought.Id, IsPreferred = true, CountryOfOrigin = "CN", HtsCode = "4016.93.5020" });

        var address = new CustomerAddress
        {
            CustomerId = customer.Id, Label = "Dock", Line1 = "9 Bow Trail", City = "Calgary", State = "AB", PostalCode = "T2P 1J9",
            Country = shipToCountry, AddressType = AddressType.Shipping, IsDefault = true,
        };
        _db.CustomerAddresses.Add(address);
        var order = new SalesOrder { OrderNumber = "SO-00042", CustomerId = customer.Id, Status = SalesOrderStatus.Confirmed };
        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SHP-00042",
            SalesOrderId = order.Id,
            ShippingAddressId = address.Id,
            Lines =
            [
                new ShipmentLine { PartId = made.Id, Quantity = 2m },
                new ShipmentLine { PartId = bought.Id, Quantity = 5m },
            ],
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        return shipment.Id;
    }

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task Cross_border_packing_slip_prints_hts_code_and_origin_per_line()
    {
        var id = await SeedShipmentAsync("CA");

        var text = Text(await new GeneratePackingSlipPdfHandler(_db, _settings).Handle(new GeneratePackingSlipPdfQuery(id), default));

        text.Should().Contain("8481.90.9040").And.Contain("4016.93.5020").And.Contain("CN").And.Contain("Origin");
    }

    [Fact]
    public async Task Cross_border_bill_of_lading_prints_hts_code_and_origin_per_line()
    {
        var id = await SeedShipmentAsync("Canada");

        var text = Text(await new GenerateBillOfLadingPdfHandler(_db, _settings).Handle(new GenerateBillOfLadingPdfQuery(id), default));

        text.Should().Contain("8481.90.9040").And.Contain("4016.93.5020").And.Contain("INTERNATIONAL");
    }

    [Fact]
    public async Task Cross_border_ship_document_lists_hts_code_and_origin_per_line()
    {
        var id = await SeedShipmentAsync("CA");
        var shipment = _db.Shipments.Single(s => s.Id == id);
        _db.Entry(shipment).Reference(s => s.SalesOrder).Load();
        _db.Entry(shipment.SalesOrder).Reference(o => o.Customer).Load();
        _db.Entry(shipment).Collection(s => s.Lines).Load();
        foreach (var line in shipment.Lines)
            _db.Entry(line).Reference(l => l.Part).Load();
        var customs = await ShipmentCustoms.LoadAsync(_db, shipment, default);

        var pdf = new ShipDocumentPdfDocument(
            shipment, QrCodeRenderer.Png("label"), "Plant", null, null, CarrierBadge.For("ups"), customs).GeneratePdf();

        customs.Should().NotBeNull();
        customs!.ShipFromCountry.Should().Be("US");
        customs.ShipToCountry.Should().Be("CA");
        Text(pdf).Should().Contain("8481.90.9040").And.Contain("4016.93.5020").And.Contain("SP-KIT-01");
    }

    [Fact]
    public async Task Domestic_shipment_documents_carry_no_customs_data()
    {
        var id = await SeedShipmentAsync("USA");

        var slip = Text(await new GeneratePackingSlipPdfHandler(_db, _settings).Handle(new GeneratePackingSlipPdfQuery(id), default));
        var bol = Text(await new GenerateBillOfLadingPdfHandler(_db, _settings).Handle(new GenerateBillOfLadingPdfQuery(id), default));

        slip.Should().NotContain("8481.90.9040");
        bol.Should().NotContain("8481.90.9040").And.NotContain("INTERNATIONAL");
    }

    public void Dispose() => _db.Dispose();
}
