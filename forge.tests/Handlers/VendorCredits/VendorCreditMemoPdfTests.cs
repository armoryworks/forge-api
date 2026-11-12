using System.Net;

using AwesomeAssertions;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.VendorCredits;

public class VendorCreditMemoPdfTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly MoneyFormatter _usd = MoneyFormat.For(null, "USD");

    public void Dispose() => _db.Dispose();

    private static VendorCreditMemo Memo()
    {
        var memo = new VendorCreditMemo
        {
            Id = 5,
            MemoNumber = "VCM-000005",
            Kind = VendorCreditMemoKind.VendorCredit,
            Status = VendorCreditMemoStatus.PartiallyApplied,
            MemoDate = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero),
            Reason = "Damaged in transit",
            VendorReference = "CR-889",
            TaxAmount = 7.25m,
            Notes = "Credit for the bent brackets.",
            Vendor = new Vendor { Id = 10, CompanyName = "Midwest Steel Supply", Email = "ar@midweststeel.example" },
            VendorBill = new VendorBill { BillNumber = "BILL-0042" },
            VendorReturn = new VendorReturn { ReturnNumber = "RTV-000003" },
        };
        memo.Lines.Add(new VendorCreditMemoLine { LineNumber = 2, Description = "Freight refund", Quantity = 1, UnitPrice = 20m });
        memo.Lines.Add(new VendorCreditMemoLine { LineNumber = 1, Description = "Bracket, formed", Quantity = 1500, UnitPrice = 0.5m });
        memo.Applications.Add(new VendorCreditMemoApplication { Amount = 100m });
        memo.Applications.Add(new VendorCreditMemoApplication { Amount = 50m, ReversedAt = DateTimeOffset.UnixEpoch });
        return memo;
    }

    [Fact]
    public void Prints_the_header_lines_and_totals()
    {
        var html = VendorCreditMemoPdfDocument.Build(Memo(), "Forge Test Works", _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().ContainAll(
            "<div class=\"company\">Forge Test Works</div>", "<div class=\"title\">VENDOR CREDIT MEMO</div>",
            "<div class=\"number\">VCM-000005</div>", "<div class=\"label\">Vendor:</div>",
            "<div class=\"strong\">Midwest Steel Supply</div>", "<div class=\"small\">ar@midweststeel.example</div>",
            "<div>Date: 10/03/2026</div>", "<div>Bill #: BILL-0042</div>", "<div>Return #: RTV-000003</div>",
            "<div>Vendor ref: CR-889</div>", "<div>Reason: Damaged in transit</div>");
        html.Should().NotContain("VOIDED");
        html.IndexOf("Bracket, formed", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Freight refund", StringComparison.Ordinal));
        html.Should().ContainAll("<td class=\"num\">1,500</td>", "<td class=\"num\">$0.50</td>", "<td class=\"num\">$750.00</td>");
        html.Should().ContainAll(
            "<span>Subtotal:</span><span>$770.00</span>", "<span>Tax:</span><span>$7.25</span>",
            "<span>Total credit:</span><span>$777.25</span>", "<span>Applied to bills:</span><span>-$100.00</span>",
            "<span>Credit remaining:</span><span>$677.25</span>");
        html.Should().Contain("<div class=\"label\">Notes</div><div class=\"body\">Credit for the bent brackets.</div>");
        html.Should().Contain("<title>VENDOR CREDIT MEMO VCM-000005</title>");
    }

    [Fact]
    public void A_voided_debit_memo_with_nothing_applied_hides_the_optional_rows()
    {
        var memo = Memo();
        memo.Kind = VendorCreditMemoKind.DebitMemo;
        memo.Status = VendorCreditMemoStatus.Voided;
        memo.TaxAmount = 0m;
        memo.VendorReference = " ";
        memo.Notes = null;
        memo.VendorBill = null;
        memo.VendorReturn = null;
        memo.Vendor.Email = null;
        memo.Applications.Clear();

        var html = VendorCreditMemoPdfDocument.Build(memo, null, _usd, VendorDocumentPdfLabels.For("en")).Html;

        html.Should().Contain("<div class=\"title\">DEBIT MEMO</div>").And.Contain("<div class=\"voided\">VOIDED</div>");
        html.Should().NotContainAny("Tax:", "Applied to bills:", "Credit remaining:", "Vendor ref:", "Bill #:", "Return #:", "class=\"notes\"", "class=\"small\"");
    }

    [Fact]
    public void Spanish_labels_and_dates()
    {
        var html = WebUtility.HtmlDecode(
            VendorCreditMemoPdfDocument.Build(Memo(), "Forge Test Works", _usd, VendorDocumentPdfLabels.For("es-US")).Html);

        html.Should().ContainAll("NOTA DE CRÉDITO DEL PROVEEDOR", "Fecha: 03/10/2026", "Factura n.º: BILL-0042", "Descripción", "Crédito total:",
            "Página <span class=\"page\"></span> de <span class=\"pages\"></span></div>");
    }

    [Fact]
    public void Text_a_user_typed_is_encoded_and_adds_no_tags()
    {
        var memo = Memo();
        memo.Vendor.CompanyName = "Steel <b>Supply</b>";
        memo.Reason = "</div><script>alert(1)</script>";
        memo.Lines.First().Description = "Refund & <b>credit</b>";

        var html = VendorCreditMemoPdfDocument.Build(memo, "Forge <Test> Works", _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().NotContain("<script").And.NotContain("<b>").And.NotContain("<Test>");
        html.Should().ContainAll(
            "Steel &lt;b&gt;Supply&lt;/b&gt;", "&lt;/div&gt;&lt;script&gt;alert(1)&lt;/script&gt;",
            "Refund &amp; &lt;b&gt;credit&lt;/b&gt;", "Forge &lt;Test&gt; Works");
    }

    [Fact]
    public void Footer_carries_the_memo_number()
    {
        var html = VendorCreditMemoPdfDocument.Build(Memo(), null, _usd, VendorDocumentPdfLabels.For(null)).Html;

        html.Should().Contain("<div class=\"page-footer\">VCM-000005 &middot; Page ");
    }

    [Fact]
    public async Task Handler_prints_the_memo_through_the_renderer()
    {
        var currency = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true };
        _db.Currencies.Add(currency);
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Forge Test Works" });
        var vendor = new Vendor { CompanyName = "Midwest Steel Supply" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var memo = new VendorCreditMemo
        {
            MemoNumber = "VCM-000009", VendorId = vendor.Id, CurrencyId = currency.Id, Reason = "Price correction",
            Status = VendorCreditMemoStatus.Open, MemoDate = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
        };
        memo.Lines.Add(new VendorCreditMemoLine { LineNumber = 1, Description = "Price correction", Quantity = 10, UnitPrice = 2m });
        _db.VendorCreditMemos.Add(memo);
        await _db.SaveChangesAsync();
        var renderer = new FakeHtmlPdfRenderer();

        var pdf = await new GenerateVendorCreditMemoPdfHandler(_db, new SystemSettingRepository(_db), renderer)
            .Handle(new GenerateVendorCreditMemoPdfQuery(memo.Id), CancellationToken.None);

        pdf.Should().StartWith("%PDF-"u8.ToArray());
        renderer.Documents.Should().ContainSingle().Which.Html
            .Should().ContainAll("VCM-000009", "Forge Test Works", "Midwest Steel Supply", "$20.00");
    }

    [Fact]
    public async Task Handler_throws_not_found_for_an_unknown_memo()
    {
        var act = () => new GenerateVendorCreditMemoPdfHandler(_db, new SystemSettingRepository(_db), new FakeHtmlPdfRenderer())
            .Handle(new GenerateVendorCreditMemoPdfQuery(31337), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*31337*");
    }
}
