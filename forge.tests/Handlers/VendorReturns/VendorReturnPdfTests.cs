using System.Net;

using AwesomeAssertions;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Features.VendorReturns;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.VendorReturns;

public class VendorReturnPdfTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly MoneyFormatter _usd = MoneyFormat.For(null, "USD");

    public void Dispose() => _db.Dispose();

    private static VendorReturn Return()
    {
        var rtv = new VendorReturn
        {
            Id = 3,
            ReturnNumber = "RTV-000003",
            RmaNumber = "RMA-7781",
            ShipDate = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
            Carrier = "UPS Ground",
            TrackingNumber = "1Z999AA10123456784",
            Notes = "Return the whole lot.",
            Vendor = new Vendor { CompanyName = "Midwest Steel Supply", Address = "100 Mill Road" },
            NonConformance = new NonConformance { NcrNumber = "NCR-0012" },
            PurchaseOrder = new PurchaseOrder { PONumber = "PO-00040" },
        };
        rtv.Lines.Add(new VendorReturnLine
        {
            LineNumber = 2, Quantity = 4, UnitPrice = 18m,
            Part = new Part { PartNumber = "BOLT-38", Description = string.Empty },
        });
        rtv.Lines.Add(new VendorReturnLine
        {
            LineNumber = 1, Quantity = 120.5m, UnitPrice = 1.2345m, LotNumber = "LOT-55",
            Part = new Part { PartNumber = "BAR-1018", Description = "1018 bar" },
            Location = new StorageLocation { Name = "QC-HOLD-1" },
        });
        return rtv;
    }

    [Fact]
    public void Prints_the_header_lines_and_expected_credit()
    {
        var html = VendorReturnPdfDocument.Build(Return(), "Forge Test Works", _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().ContainAll(
            "<div class=\"company\">Forge Test Works</div>", "<div class=\"title\">RETURN TO VENDOR</div>",
            "<div class=\"number\">RTV-000003</div>", "<div class=\"label\">Ship to:</div>",
            "<div class=\"strong\">Midwest Steel Supply</div>", "<div class=\"small\">100 Mill Road</div>",
            "<div>NCR #: NCR-0012</div>", "<div>PO #: PO-00040</div>", "<div>Vendor RMA #: RMA-7781</div>",
            "<div>Ship date: 10/07/2026</div>", "<div>Carrier: UPS Ground</div>", "<div>Tracking #: 1Z999AA10123456784</div>");
        WebUtility.HtmlDecode(html).Should().Contain("<td>BAR-1018 — 1018 bar</td>");
        html.Should().ContainAll("<td>BOLT-38</td>", "<td>LOT-55</td>", "<td>QC-HOLD-1</td>", "<td class=\"num\">120.5</td>", "<td class=\"num\">$18.00</td>");
        html.IndexOf("BAR-1018", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("BOLT-38", StringComparison.Ordinal));
        html.Should().Contain("<span>Expected credit:</span><span>$220.76</span>");
        html.Should().Contain("<div class=\"label\">Notes</div><div class=\"body\">Return the whole lot.</div>");
        html.Should().ContainAll("Packed by: ____________________", "Received by vendor: ____________________");
        html.Should().Contain("<title>RETURN TO VENDOR RTV-000003</title>");
    }

    [Fact]
    public void A_bare_return_hides_the_optional_lines()
    {
        var rtv = Return();
        rtv.RmaNumber = null;
        rtv.ShipDate = null;
        rtv.Carrier = " ";
        rtv.TrackingNumber = null;
        rtv.Notes = null;
        rtv.NonConformance = null;
        rtv.PurchaseOrder = null;
        rtv.Vendor.Address = null;

        var html = VendorReturnPdfDocument.Build(rtv, null, _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().NotContainAny("NCR #:", "PO #:", "Vendor RMA #:", "Ship date:", "Carrier:", "Tracking #:", "class=\"notes\"", "class=\"small\"");
        html.Should().Contain("Packed by:");
    }

    [Fact]
    public void Spanish_labels_and_dates()
    {
        var html = WebUtility.HtmlDecode(
            VendorReturnPdfDocument.Build(Return(), null, _usd, VendorDocumentPdfLabels.For("es")).Html);

        html.Should().ContainAll("DEVOLUCIÓN AL PROVEEDOR", "Fecha de envío: 07/10/2026", "Desde ubicación", "Crédito esperado:", "Empacado por:");
    }

    [Fact]
    public void Text_a_user_typed_is_encoded_and_adds_no_tags()
    {
        var rtv = Return();
        rtv.Vendor.CompanyName = "Steel <b>Supply</b>";
        rtv.Notes = "</div><script>alert(1)</script>";
        rtv.Carrier = "Truck & <b>trailer</b>";

        var html = VendorReturnPdfDocument.Build(rtv, null, _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().ContainAll(
            "Steel &lt;b&gt;Supply&lt;/b&gt;", "&lt;/div&gt;&lt;script&gt;alert(1)&lt;/script&gt;", "Truck &amp; &lt;b&gt;trailer&lt;/b&gt;");
    }

    [Fact]
    public void Footer_carries_the_return_number()
    {
        var html = VendorReturnPdfDocument.Build(Return(), null, _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().Contain("<div class=\"page-footer\">RTV-000003 &middot; Page ");
    }

    [Fact]
    public async Task Handler_prints_the_return_through_the_renderer()
    {
        var currency = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true };
        _db.Currencies.Add(currency);
        var vendor = new Vendor { CompanyName = "Midwest Steel Supply" };
        _db.Vendors.Add(vendor);
        var part = new Part { PartNumber = "BAR-1018", Name = "1018 bar" };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        var rtv = new VendorReturn { ReturnNumber = "RTV-000009", VendorId = vendor.Id, CurrencyId = currency.Id };
        rtv.Lines.Add(new VendorReturnLine { LineNumber = 1, PartId = part.Id, Quantity = 10, UnitPrice = 2m });
        _db.VendorReturns.Add(rtv);
        await _db.SaveChangesAsync();
        var renderer = new FakeHtmlPdfRenderer();

        var pdf = await new GenerateVendorReturnPdfHandler(_db, new SystemSettingRepository(_db), renderer)
            .Handle(new GenerateVendorReturnPdfQuery(rtv.Id), CancellationToken.None);

        pdf.Should().StartWith("%PDF-"u8.ToArray());
        renderer.Documents.Should().ContainSingle().Which.Html
            .Should().ContainAll("RTV-000009", "Midwest Steel Supply", "BAR-1018", "$20.00");
    }

    [Fact]
    public async Task Handler_throws_not_found_for_an_unknown_return()
    {
        var act = () => new GenerateVendorReturnPdfHandler(_db, new SystemSettingRepository(_db), new FakeHtmlPdfRenderer())
            .Handle(new GenerateVendorReturnPdfQuery(31337), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*31337*");
    }
}
