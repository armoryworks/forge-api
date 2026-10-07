using System.Text;

using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.PurchaseOrders;

public class GetPurchaseOrderPdfTests
{
    static GetPurchaseOrderPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task<PurchaseOrder> SeedAsync(PurchaseOrderStatus status)
    {
        var vendor = new Vendor
        {
            Id = 10,
            CompanyName = "Midwest Steel Supply",
            VendorNumber = "VEND-00010",
            ContactName = "Pat Buyer",
            Phone = "555-0100",
            Email = "orders@midweststeel.example",
            Address = "100 Mill Road",
            City = "Gary",
            State = "IN",
            ZipCode = "46402",
            PaymentTerms = "Net 30",
        };
        _db.Vendors.Add(vendor);
        _db.UnitsOfMeasure.Add(new UnitOfMeasure { Id = 3, Code = "LB", Name = "Pound" });
        _db.Parts.Add(new Part { Id = 20, PartNumber = "BAR-1018", Name = "1018 bar", PurchaseUomId = 3 });
        _db.Parts.Add(new Part { Id = 21, PartNumber = "BOLT-38", Name = "3/8 bolt" });
        _db.PartPurchaseUnits.Add(new PartPurchaseUnit { Id = 30, PartId = 21, Label = "Box of 100", ContentQuantity = 100 });
        _db.VendorParts.Add(new VendorPart { VendorId = 10, PartId = 20, VendorPartNumber = "MSS-1018-12", Currency = "USD" });
        _db.VendorParts.Add(new VendorPart { VendorId = 99, PartId = 21, VendorPartNumber = "OTHER-VENDOR", Currency = "USD" });

        var po = new PurchaseOrder
        {
            Id = 40,
            PONumber = "PO-00040",
            VendorId = 10,
            Status = status,
            ExpectedDeliveryDate = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            Notes = "Deliver to receiving dock B.",
            EstimatedFreight = 45m,
            QuoteCurrency = "USD",
        };
        po.Lines.Add(new PurchaseOrderLine { Id = 1, PartId = 20, Description = "1018 cold-rolled bar, 1in", OrderedQuantity = 120.5m, UnitPrice = 1.2345m });
        po.Lines.Add(new PurchaseOrderLine { Id = 2, PartId = 21, Description = "Hex bolt 3/8-16", OrderedQuantity = 4, UnitPrice = 18m, PurchaseUnitId = 30, Notes = "Zinc plated" });
        po.Lines.Add(new PurchaseOrderLine { Id = 3, Description = "Saw blade sharpening", OrderedQuantity = 1, UnitPrice = 60m });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();
        return po;
    }

    private GetPurchaseOrderPdfHandler Handler() => new(_db);

    [Fact]
    public async Task Renders_a_pdf_for_a_draft_po()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);

        var pdf = await Handler().Handle(new GetPurchaseOrderPdfQuery(40), CancellationToken.None);

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Draft_po_is_watermarked_and_a_submitted_one_is_not()
    {
        await SeedAsync(PurchaseOrderStatus.Draft);
        var draft = await Handler().BuildDocumentAsync(40, CancellationToken.None);
        draft.IsDraft.Should().BeTrue();

        var po = await _db.PurchaseOrders.FindAsync(40);
        po!.Status = PurchaseOrderStatus.Submitted;
        await _db.SaveChangesAsync();
        var submitted = await Handler().BuildDocumentAsync(40, CancellationToken.None);
        submitted.IsDraft.Should().BeFalse();
        submitted.GeneratePdf().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Lines_carry_this_vendors_part_number_and_the_ordered_unit()
    {
        await SeedAsync(PurchaseOrderStatus.Submitted);
        var document = await Handler().BuildDocumentAsync(40, CancellationToken.None);
        var lines = (await _db.PurchaseOrders.FindAsync(40))!.Lines.OrderBy(l => l.Id).ToList();

        document.VendorPartNumberFor(lines[0]).Should().Be("MSS-1018-12");
        document.VendorPartNumberFor(lines[1]).Should().BeEmpty("the only vendor part number on file belongs to another vendor");
        document.VendorPartNumberFor(lines[2]).Should().BeEmpty();
        PurchaseOrderPdfDocument.UomFor(lines[0]).Should().Be("LB");
        PurchaseOrderPdfDocument.UomFor(lines[1]).Should().Be("Box of 100");
        PurchaseOrderPdfDocument.UomFor(lines[2]).Should().BeEmpty();
    }

    [Fact]
    public void Money_is_shown_in_the_quote_currency()
    {
        PurchaseOrderPdfDocument.Money(1234.5m, "EUR").Should().Be("1,234.50 EUR");
        PurchaseOrderPdfDocument.UnitPrice(1.2345m).Should().Be("1.2345");
        PurchaseOrderPdfDocument.UnitPrice(18m).Should().Be("18.00");
    }

    [Fact]
    public async Task Uses_the_company_name_setting_before_the_legacy_one()
    {
        await SeedAsync(PurchaseOrderStatus.Submitted);
        _db.SystemSettings.Add(new SystemSetting { Key = "company_name", Value = "Legacy Name" });
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Current Name" });
        await _db.SaveChangesAsync();

        var document = await Handler().BuildDocumentAsync(40, CancellationToken.None);

        document.GetMetadata().Author.Should().Be("Current Name");
    }

    [Fact]
    public async Task Falls_back_to_the_legacy_company_name_and_renders_without_one()
    {
        await SeedAsync(PurchaseOrderStatus.Submitted);

        var unnamed = await Handler().BuildDocumentAsync(40, CancellationToken.None);
        unnamed.GetMetadata().Author.Should().BeEmpty();
        unnamed.GeneratePdf().Should().NotBeEmpty();

        _db.SystemSettings.Add(new SystemSetting { Key = "company_name", Value = "Legacy Name" });
        await _db.SaveChangesAsync();
        var legacy = await Handler().BuildDocumentAsync(40, CancellationToken.None);
        legacy.GetMetadata().Author.Should().Be("Legacy Name");
    }

    [Fact]
    public async Task Renders_with_a_ship_to_location_and_a_bare_vendor()
    {
        _db.Vendors.Add(new Vendor { Id = 11, CompanyName = "Cash Vendor" });
        _db.CompanyLocations.Add(new CompanyLocation
        {
            Name = "Main Plant", Line1 = "1 Factory Way", City = "Ogden", State = "UT", PostalCode = "84401",
            Phone = "555-0199", IsDefault = true,
        });
        var po = new PurchaseOrder { Id = 41, PONumber = "PO-00041", VendorId = 11, Status = PurchaseOrderStatus.Acknowledged, QuoteCurrency = "CAD" };
        po.Lines.Add(new PurchaseOrderLine { Id = 9, Description = "Consulting", OrderedQuantity = 2, UnitPrice = 150m });
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        var pdf = await Handler().Handle(new GetPurchaseOrderPdfQuery(41), CancellationToken.None);

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Unknown_po_throws_not_found()
    {
        var act = () => Handler().Handle(new GetPurchaseOrderPdfQuery(31337), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*31337*");
    }
}
