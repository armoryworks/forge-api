using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

using Forge.Api.Features.Customers;
using Forge.Api.Features.Invoices;
using Forge.Api.Features.Quotes;
using Forge.Api.Features.VendorBills;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Invoices;

public class InvoiceCurrencyFormattingTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Currency _usd = new() { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true };
    private readonly Currency _cad = new() { Code = "CAD", Name = "Canadian Dollar", Symbol = "$", DecimalPlaces = 2 };

    public InvoiceCurrencyFormattingTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _db.Currencies.AddRange(_usd, _cad);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static string Svg(IDocument document) => string.Join("\n", document.GenerateSvg());

    private async Task<Invoice> SeedInvoiceAsync(Customer customer, Currency currency, string number, decimal unitPrice)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = number,
            CustomerId = customer.Id,
            CurrencyId = currency.Id,
            Status = InvoiceStatus.Sent,
            TaxRate = 0.0725m,
            InvoiceDate = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            DueDate = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero),
            Lines = { new InvoiceLine { LineNumber = 1, Description = "Bracket", Quantity = 1940.0000m, UnitPrice = unitPrice } },
        };
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = new Customer { Name = "Buyer Co", Email = "ap@buyer.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task InvoicePdf_InForeignCurrency_PrintsItsCode_FractionalTax_AndGroupedQuantity()
    {
        var invoice = await SeedInvoiceAsync(await SeedCustomerAsync(), _cad, "INV-5100", 10m);
        var money = await MoneyFormat.ForCurrencyIdAsync(_db, invoice.CurrencyId, CancellationToken.None);

        var html = InvoicePdfDocument.Build(invoice, "Harbor Gear Works", money).Html;

        html.Should().Contain("$19,400.00 CAD");
        html.Should().Contain("7.25%");
        html.Should().Contain("1,940");
        html.Should().NotContain("1940.0000");
        html.Should().NotContain("¤");
        html.Should().NotContain("#INV-5100");
    }

    [Fact]
    public async Task InvoicePdf_WithNonLatinNames_RendersThem()
    {
        var customer = new Customer { Name = "株式会社東京製作所 شركة الخليج 🔧", Email = "ap@buyer.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var invoice = await SeedInvoiceAsync(customer, _usd, "INV-5102", 1m);

        var html = InvoicePdfDocument.Build(invoice, "Harbor Gear Works").Html;

        System.Net.WebUtility.HtmlDecode(html).Should().Contain("株式会社東京製作所 شركة الخليج 🔧");
    }

    [Fact]
    public async Task InvoicePdf_EncodesEveryValueAUserTyped()
    {
        var customer = new Customer { Name = "<img src=x onerror=alert(1)>", Email = "ap@buyer.test" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        var invoice = await SeedInvoiceAsync(customer, _usd, "INV-5103", 1m);
        invoice.Notes = "</div><script>fetch('http://internal/')</script>";
        invoice.Lines.First().Description = "Bracket <b>bold</b>";

        var document = InvoicePdfDocument.Build(invoice, "Harbor <Gear> Works");

        document.Html.Should().NotContain("<img").And.NotContain("<script").And.NotContain("<b>bold");
        document.Html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;").And.Contain("Bracket &lt;b&gt;bold&lt;/b&gt;").And.Contain("Harbor &lt;Gear&gt; Works");
        document.Html.Should().Contain("<div class=\"page-footer\">INV-5103 &middot; Page ");
    }

    [Fact]
    public async Task GenerateInvoicePdf_PrintsTheInvoiceThroughTheRenderer()
    {
        var invoice = await SeedInvoiceAsync(await SeedCustomerAsync(), _cad, "INV-5104", 10m);
        var renderer = new FakeHtmlPdfRenderer();

        var pdf = await new GenerateInvoicePdfHandler(_db, new SystemSettingRepository(_db), renderer)
            .Handle(new GenerateInvoicePdfQuery(invoice.Id), CancellationToken.None);

        var printed = renderer.Documents.Should().ContainSingle().Subject;
        printed.Html.Should().Contain("INV-5104").And.Contain("Invoice Date:").And.Contain("Total:").And.Contain("CAD");
        pdf.Should().StartWith("%PDF-"u8.ToArray());
    }

    [Fact]
    public async Task GenerateInvoicePdf_LoadsTheInvoiceCurrency()
    {
        var invoice = await SeedInvoiceAsync(await SeedCustomerAsync(), _cad, "INV-5101", 10m);

        var pdf = await new GenerateInvoicePdfHandler(_db, new SystemSettingRepository(_db), new FakeHtmlPdfRenderer())
            .Handle(new GenerateInvoicePdfQuery(invoice.Id), CancellationToken.None);

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void QuotePdf_PrintsFractionalTaxAndNoHashPrefix()
    {
        var quote = new Quote
        {
            QuoteNumber = "Q-0042",
            Customer = new Customer { Name = "Buyer Co" },
            TaxRate = 0.0725m,
            Lines = { new QuoteLine { LineNumber = 1, Description = "Bracket", Quantity = 1940m, UnitPrice = 1m } },
        };

        var svg = Svg(new QuotePdfDocument(quote, "Harbor Gear Works", [], MoneyFormat.For(_usd, "USD")));

        svg.Should().Contain("7.25%");
        svg.Should().Contain("$1,940.00");
        svg.Should().NotContain("#Q-0042");
        svg.Should().NotContain("¤");
    }

    [Fact]
    public async Task Statement_FormatsEachInvoiceInItsCurrency_AndTotalsPerCurrency()
    {
        var customer = await SeedCustomerAsync();
        await SeedInvoiceAsync(customer, _usd, "INV-5200", 1m);
        await SeedInvoiceAsync(customer, _cad, "INV-5201", 2m);

        var pdf = await new GenerateCustomerStatementHandler(_db, new SystemClock())
            .Handle(new GenerateCustomerStatementQuery(customer.Id), CancellationToken.None);
        pdf.Should().NotBeEmpty();

        var invoices = await _db.Invoices.Include(i => i.Lines).Include(i => i.PaymentApplications).ToListAsync();
        var currencyMoney = new Dictionary<int, MoneyFormatter>
        {
            [_usd.Id] = MoneyFormat.For(_usd, "USD"),
            [_cad.Id] = MoneyFormat.For(_cad, "USD"),
        };
        var svg = Svg(new CustomerStatementPdfDocument(
            customer, invoices, [], null, DateTimeOffset.UnixEpoch, currencyMoney, null, currencyMoney[_usd.Id]));

        svg.Should().Contain("$2,080.65");
        svg.Should().Contain("$4,161.30 CAD");
        svg.Should().NotContain("$6,241.95");
        svg.Should().NotContain("¤");
    }

    [Fact]
    public async Task InvoiceList_AndDetail_CarryTheCurrencyCode()
    {
        var invoice = await SeedInvoiceAsync(await SeedCustomerAsync(), _cad, "INV-5300", 1m);
        var repo = new InvoiceRepository(_db);

        var list = await repo.GetPagedAsync(new InvoiceListQuery(), CancellationToken.None);
        var detail = await new GetInvoiceByIdHandler(repo)
            .Handle(new GetInvoiceByIdQuery(invoice.Id), CancellationToken.None);

        list.Items.Single().CurrencyCode.Should().Be("CAD");
        detail.CurrencyCode.Should().Be("CAD");
    }

    [Fact]
    public async Task CreateVendorBill_ActivityText_UsesTheBillCurrency()
    {
        var vendor = new Vendor { CompanyName = "Steel Supply" };
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();
        var handler = new CreateVendorBillHandler(new VendorBillRepository(_db), new VendorRepository(_db), _db);

        var bill = await handler.Handle(new CreateVendorBillCommand(
            vendor.Id, "V-1", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0m, null,
            [new CreateVendorBillLineModel(null, "Steel", 2m, 600m, null)],
            CurrencyId: _cad.Id, FxRate: 1m), CancellationToken.None);

        var log = await _db.ActivityLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == "VendorBill" && a.EntityId == bill.Id && a.Action == "created");
        log.Description.Should().Contain("$1,200.00 CAD");
        log.Description.Should().NotContain("¤");
    }
}
