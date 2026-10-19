using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Infrastructure;
using SkiaSharp;

using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Shipments;

public class ShippingLabelImageTests : IDisposable
{
    static ShippingLabelImageTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly MockStorageService _storage = new(NullLogger<MockStorageService>.Instance);

    public void Dispose() => _db.Dispose();

    private async Task<Shipment> SeedAsync(string serviceId)
    {
        var customer = new Customer { Name = "Acme" };
        var carrier = new Carrier
        {
            Name = serviceId.ToUpperInvariant(),
            IntegrationKind = CarrierIntegrationKind.Api,
            IntegrationServiceId = serviceId,
        };
        _db.AddRange(customer, carrier);
        _db.CompanyLocations.Add(new CompanyLocation
        {
            Name = "Plant", Line1 = "1 Mill Rd", City = "Ogden", State = "UT", PostalCode = "84401", IsDefault = true,
        });
        await _db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            CustomerId = customer.Id, Label = "Dock", Line1 = "9 Pier St", City = "Boise", State = "ID", PostalCode = "83702",
        };
        var order = new SalesOrder { OrderNumber = "SO-0001", CustomerId = customer.Id };
        _db.AddRange(address, order);
        await _db.SaveChangesAsync();

        var shipment = new Shipment
        {
            ShipmentNumber = "SH-0001", SalesOrderId = order.Id, CarrierId = carrier.Id, ShippingAddressId = address.Id,
        };
        _db.Shipments.Add(shipment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return shipment;
    }

    private CreateShippingLabelHandler LabelHandler(IShippingService shipping)
        => new(new ShipmentRepository(_db), shipping, _storage, _db);

    private ShipDocumentComposer Composer()
    {
        var settings = new Mock<ISystemSettingRepository>();
        return new ShipDocumentComposer(_db, _storage, settings.Object);
    }

    [Fact]
    public async Task Mock_carrier_returns_a_4x6_png_label()
    {
        var label = await new MockShippingService(NullLogger<MockShippingService>.Instance)
            .CreateLabelAsync(new ShipmentRequest(null!, null!, [], null), "ups", CancellationToken.None);

        label.LabelBytes.Should().NotBeNull();
        ShipLabelStorage.IsPng(label.LabelBytes).Should().BeTrue();
        using var bitmap = SKBitmap.Decode(label.LabelBytes);
        bitmap.Width.Should().Be(400);
        bitmap.Height.Should().Be(600);
    }

    [Fact]
    public async Task Ship_document_builds_from_a_mock_carrier_label()
    {
        var shipment = await SeedAsync("ups");

        await LabelHandler(new MockShippingService(NullLogger<MockShippingService>.Instance))
            .Handle(new CreateShippingLabelCommand(shipment.Id), CancellationToken.None);
        var composed = await Composer().ComposeAsync(shipment.Id, CancellationToken.None);

        Encoding.ASCII.GetString(composed.Pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task A_pdf_label_is_not_stored_as_the_png_and_the_ship_document_says_why()
    {
        var shipment = await SeedAsync("usps");
        var shipping = new Mock<IShippingService>();
        shipping.Setup(s => s.CreateLabelAsync(It.IsAny<ShipmentRequest>(), "usps", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShippingLabel("9400100000000000000000", "data:application/pdf;base64,JVBERi0=", "USPS",
                Encoding.ASCII.GetBytes("%PDF-1.7")));

        await LabelHandler(shipping.Object).Handle(new CreateShippingLabelCommand(shipment.Id), CancellationToken.None);

        var stored = () => _storage.DownloadAsync(ShipLabelStorage.Bucket, ShipLabelStorage.Key(shipment.Id), CancellationToken.None);
        await stored.Should().ThrowAsync<KeyNotFoundException>();
        var compose = () => Composer().ComposeAsync(shipment.Id, CancellationToken.None);
        await compose.Should().ThrowAsync<InvalidOperationException>().WithMessage("*returns its label as a PDF*");
    }

    [Fact]
    public async Task Ship_document_before_any_label_asks_for_the_label_first()
    {
        var shipment = await SeedAsync("ups");

        var compose = () => Composer().ComposeAsync(shipment.Id, CancellationToken.None);

        await compose.Should().ThrowAsync<InvalidOperationException>().WithMessage("Create the shipping label first*");
    }

    [Fact]
    public void Png_signature_check_rejects_other_formats()
    {
        ShipLabelStorage.IsPng([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]).Should().BeTrue();
        ShipLabelStorage.IsPng(Encoding.ASCII.GetBytes("%PDF-1.7")).Should().BeFalse();
        ShipLabelStorage.IsPng(Encoding.ASCII.GetBytes("^XA^FO50")).Should().BeFalse();
        ShipLabelStorage.IsPng([]).Should().BeFalse();
    }
}
