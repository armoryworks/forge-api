using System.Text;

using AwesomeAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;

using Forge.Api.Controllers;
using Forge.Api.Features.Purchasing;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Purchasing;

public class GetRfqPdfTests
{
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

    private readonly FakeHtmlPdfRenderer _renderer = new();

    private GetRfqPdfHandler Handler() => new(_db, _renderer);

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

        unaddressed.Html.Should().NotContain("Midwest Steel Supply").And.NotContain("<div class=\"caption\">TO</div>");
        addressed.Html.Should().Contain("<div class=\"caption\">TO</div>").And.Contain("<div class=\"name\">Midwest Steel Supply</div>");
        addressed.Html.Should().ContainAll(
            "<div class=\"small\">100 Mill Road</div>", "<div class=\"small\">Gary, IN 46402</div>",
            "<div class=\"small gap\">Attn: Pat Buyer</div>", "<div>Vendor #: VEND-00010</div>");
        addressed.Html.Should().Contain("<td>Formed steel bracket</td>").And.Contain("<td>EA</td>").And.Contain("<td class=\"num\">250</td>");
        addressed.Html.Should().Contain("<title>Request for Quote RFQ-00007</title>");
        addressed.Html.Should().Contain("<div class=\"name\">Forge Test Works</div>");
        addressed.Html.Should().ContainAll(
            "Response Due: 10/20/2026", "Required By: 11/30/2026", "Annual bracket buy", "Quote with and without powder coat.",
            "Responses are due by 10/20/2026.", "Please reference RFQ RFQ-00007 on your quotation.");
    }

    [Fact]
    public async Task Renders_without_company_settings_location_or_uom()
    {
        _db.Parts.Add(new Part { Id = 21, PartNumber = "MISC", Name = "Misc part" });
        _db.RequestForQuotes.Add(new RequestForQuote { Id = 8, RfqNumber = "RFQ-00008", PartId = 21, Quantity = 1m });
        await _db.SaveChangesAsync();

        var document = await Handler().BuildDocumentAsync(8, null, CancellationToken.None);

        document.Html.Should().Contain("<td>Misc part</td>").And.Contain("<td></td>");
        document.Html.Should().NotContain("Response Due:").And.NotContain("Special Instructions").And.NotContain("class=\"name\"");
    }

    [Fact]
    public async Task Text_a_user_typed_is_encoded_and_adds_no_tags()
    {
        await SeedAsync();
        var vendor = await _db.Vendors.FindAsync(10);
        vendor!.CompanyName = "Steel <b>Supply</b>";
        var rfq = await _db.RequestForQuotes.FindAsync(7);
        rfq!.SpecialInstructions = "</div><script>alert(1)</script>";
        rfq.Description = "Brackets & <b>braces</b>";
        await _db.SaveChangesAsync();

        var html = (await Handler().BuildDocumentAsync(7, 10, CancellationToken.None)).Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("Steel &lt;b&gt;Supply&lt;/b&gt;").And.Contain("&lt;/div&gt;&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("Brackets &amp; &lt;b&gt;braces&lt;/b&gt;");
    }

    [Fact]
    public async Task Prints_through_the_renderer_with_the_rfq_number_in_the_footer()
    {
        await SeedAsync();

        await Handler().Handle(new GetRfqPdfQuery(7, 10), CancellationToken.None);

        _renderer.Documents.Should().ContainSingle().Which.Html
            .Should().Contain("<div class=\"page-footer\">RFQ-00007 &middot; Page ");
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
