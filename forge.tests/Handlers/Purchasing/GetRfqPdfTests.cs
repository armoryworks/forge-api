using System.Text;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

using Forge.Api.Controllers;
using Forge.Api.Features.Purchasing;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Purchasing;

public class GetRfqPdfTests
{
    static GetRfqPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly AppDbContext _db = TestDbContextFactory.Create();

    private async Task SeedAsync()
    {
        _db.UnitsOfMeasure.Add(new UnitOfMeasure { Id = 3, Code = "EA", Name = "Each" });
        _db.Parts.Add(new Part { Id = 20, PartNumber = "BRK-100", Name = "Bracket", Description = "Formed steel bracket", StockUomId = 3 });
        _db.Vendors.Add(new Vendor
        {
            Id = 10,
            CompanyName = "Midwest Steel Supply",
            VendorNumber = "VEND-00010",
            ContactName = "Pat Buyer",
            Address = "100 Mill Road",
            City = "Gary",
            State = "IN",
            ZipCode = "46402",
        });
        _db.CompanyLocations.Add(new CompanyLocation
        {
            Name = "Main Plant", Line1 = "1 Factory Way", City = "Ogden", State = "UT", PostalCode = "84401", IsDefault = true,
        });
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Forge Test Works" });
        var rfq = new RequestForQuote
        {
            Id = 7,
            RfqNumber = "RFQ-00007",
            PartId = 20,
            Quantity = 250m,
            RequiredDate = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero),
            ResponseDeadline = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            Status = RfqStatus.Sent,
            Description = "Annual bracket buy",
            SpecialInstructions = "Quote with and without powder coat.",
        };
        rfq.VendorResponses.Add(new RfqVendorResponse { VendorId = 10 });
        _db.RequestForQuotes.Add(rfq);
        await _db.SaveChangesAsync();
    }

    private GetRfqPdfHandler Handler() => new(_db);

    [Fact]
    public async Task Renders_a_pdf_for_the_rfq()
    {
        await SeedAsync();

        var pdf = await Handler().Handle(new GetRfqPdfQuery(7), CancellationToken.None);

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Is_addressed_to_the_requested_vendor()
    {
        await SeedAsync();

        var unaddressed = await Handler().BuildDocumentAsync(7, null, CancellationToken.None);
        var addressed = await Handler().BuildDocumentAsync(7, 10, CancellationToken.None);

        unaddressed.AddressedTo.Should().BeNull();
        addressed.AddressedTo!.CompanyName.Should().Be("Midwest Steel Supply");
        addressed.LineDescription.Should().Be("Formed steel bracket");
        addressed.Uom.Should().Be("EA");
        addressed.GetMetadata().Title.Should().Be("Request for Quote RFQ-00007");
        addressed.GetMetadata().Author.Should().Be("Forge Test Works");
        addressed.GeneratePdf().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Renders_without_company_settings_location_or_uom()
    {
        _db.Parts.Add(new Part { Id = 21, PartNumber = "MISC", Name = "Misc part" });
        _db.RequestForQuotes.Add(new RequestForQuote { Id = 8, RfqNumber = "RFQ-00008", PartId = 21, Quantity = 1m });
        await _db.SaveChangesAsync();

        var document = await Handler().BuildDocumentAsync(8, null, CancellationToken.None);

        document.LineDescription.Should().Be("Misc part");
        document.Uom.Should().BeEmpty();
        document.GeneratePdf().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Unknown_rfq_or_vendor_throws_not_found()
    {
        await SeedAsync();

        var missingRfq = () => Handler().Handle(new GetRfqPdfQuery(31337), CancellationToken.None);
        var missingVendor = () => Handler().Handle(new GetRfqPdfQuery(7, 999), CancellationToken.None);

        await missingRfq.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*31337*");
        await missingVendor.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*999*");
    }

    [Fact]
    public async Task Endpoint_returns_application_pdf()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetRfqPdfQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("%PDF-1.7"u8.ToArray());
        var controller = new PurchasingController(mediator.Object);

        var result = await controller.GetRfqPdf(7, 10, CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be("rfq-7-vendor-10.pdf");
        mediator.Verify(m => m.Send(new GetRfqPdfQuery(7, 10), It.IsAny<CancellationToken>()), Times.Once);
    }
}
