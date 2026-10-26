using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Integrations;

public class QuickBooksDocumentPayloadTests
{
    private sealed class CapturingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (QuickBooksAccountingService Service, CapturingHandler Handler) Create(
        string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resolver = new Mock<IExternalIdentityResolver>();
        resolver
            .Setup(r => r.ResolveAsync("quickbooks", null, It.IsAny<TokenResolutionPolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedExternalIdentity("token", "quickbooks", TokenScope.Install, null, "realm-1"));

        var handler = new CapturingHandler(responseJson, status);
        var service = new QuickBooksAccountingService(
            resolver.Object,
            new Forge.Tests.Helpers.StubHttpClientFactory(handler),
            Options.Create(new QuickBooksOptions()),
            NullLogger<QuickBooksAccountingService>.Instance);
        return (service, handler);
    }

    private static List<decimal> LineAmounts(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Line").EnumerateArray()
            .Select(l => l.GetProperty("Amount").GetDecimal())
            .ToList();
    }

    private static AccountingDocument Document(AccountingDocumentType type) => new(
        type,
        "cust-1",
        [
            new AccountingLineItem("Half-cent line", 1m, 10.125m, null),
            new AccountingLineItem("Fractional qty", 2.5m, 0.333m, null),
        ],
        "INV-1",
        10.96m,
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateInvoiceAsync_SendsLineAmountsRoundedHalfAwayFromZeroToTheCent()
    {
        var (service, handler) = Create("""{"Invoice":{"Id":"42"}}""");

        await service.CreateInvoiceAsync(Document(AccountingDocumentType.Invoice), CancellationToken.None);

        LineAmounts(handler.Bodies.Single()).Should().Equal(10.13m, 0.83m);
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_SendsLineAmountsRoundedHalfAwayFromZeroToTheCent()
    {
        var (service, handler) = Create("""{"PurchaseOrder":{"Id":"7"}}""");

        await service.CreatePurchaseOrderAsync(Document(AccountingDocumentType.PurchaseOrder), CancellationToken.None);

        LineAmounts(handler.Bodies.Single()).Should().Equal(10.13m, 0.83m);
    }

    [Fact]
    public async Task CreateInvoiceAsync_ForeignDocument_SendsCurrencyRefAndExchangeRate()
    {
        var (service, handler) = Create("""{"Invoice":{"Id":"43"}}""");

        await service.CreateInvoiceAsync(
            Document(AccountingDocumentType.Invoice) with { CurrencyCode = "CAD", ExchangeRate = 0.73m },
            CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        doc.RootElement.GetProperty("CurrencyRef").GetProperty("value").GetString().Should().Be("CAD");
        doc.RootElement.GetProperty("ExchangeRate").GetDecimal().Should().Be(0.73m);
    }

    [Fact]
    public async Task CreateInvoiceAsync_HomeCurrencyDocument_SendsNoCurrencyRef()
    {
        var (service, handler) = Create("""{"Invoice":{"Id":"44"}}""");

        await service.CreateInvoiceAsync(Document(AccountingDocumentType.Invoice), CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        doc.RootElement.TryGetProperty("CurrencyRef", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("ExchangeRate", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateInvoiceAsync_ForeignDocumentRefusedForCurrency_ExplainsMulticurrency()
    {
        var (service, _) = Create(
            """{"Fault":{"Error":[{"Message":"Invalid Reference Id","Detail":"Currency CAD is not enabled"}],"type":"ValidationFault"}}""",
            HttpStatusCode.BadRequest);

        var act = () => service.CreateInvoiceAsync(
            Document(AccountingDocumentType.Invoice) with { CurrencyCode = "CAD", ExchangeRate = 0.73m },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().StartWith("QuickBooks multicurrency is off");
    }

    private static AccountingDocument FreightInvoice(string? freightItemId, bool taxable) => new(
        AccountingDocumentType.Invoice,
        "cust-1",
        [
            new AccountingLineItem("Bracket", 2m, 50m, null),
            new AccountingLineItem("Freight", 1m, 25m, freightItemId, IsFreight: true, Taxable: taxable),
        ],
        "INV-2",
        125m,
        new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    private static JsonElement SalesDetail(string body, int index)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("Line")[index].GetProperty("SalesItemLineDetail").Clone();
    }

    [Fact]
    public async Task CreateInvoiceAsync_FreightLine_UsesTheMappedFreightItemAndPassesItsTaxCode()
    {
        var (service, handler) = Create("""{"Invoice":{"Id":"45"}}""");

        await service.CreateInvoiceAsync(FreightInvoice("77", taxable: false), CancellationToken.None);

        var body = handler.Bodies.Single();
        var freight = SalesDetail(body, 1);
        freight.GetProperty("ItemRef").GetProperty("value").GetString().Should().Be("77");
        freight.GetProperty("TaxCodeRef").GetProperty("value").GetString().Should().Be("NON");
        SalesDetail(body, 0).GetProperty("TaxCodeRef").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void BuildDocumentPayload_UnmappedFreightLine_GoesOnTheShippingItem()
    {
        var body = JsonSerializer.Serialize(
            QuickBooksAccountingService.BuildDocumentPayload(FreightInvoice(null, taxable: true)));

        var freight = SalesDetail(body, 1);
        freight.GetProperty("ItemRef").GetProperty("value").GetString().Should().Be(QuickBooksAccountingService.ShippingItemId);
        freight.GetProperty("TaxCodeRef").GetProperty("value").GetString().Should().Be("TAX");
    }

    [Fact]
    public async Task CreatePurchaseOrderAsync_SendsNoTaxCodeRef()
    {
        var (service, handler) = Create("""{"PurchaseOrder":{"Id":"8"}}""");

        await service.CreatePurchaseOrderAsync(Document(AccountingDocumentType.PurchaseOrder), CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        doc.RootElement.GetProperty("Line")[0].GetProperty("ItemBasedExpenseLineDetail")
            .TryGetProperty("TaxCodeRef", out _).Should().BeFalse();
    }

    [Fact]
    public void Term_payload_carries_due_days_and_the_early_payment_discount()
    {
        var discount = QuickBooksAccountingService.BuildTermPayload(new AccountingTerms("2/10 Net 30", 30, 2m, 10));
        discount["Type"].Should().Be("STANDARD");
        discount["DueDays"].Should().Be(30);
        discount["DiscountPercent"].Should().Be(2m);
        discount["DiscountDays"].Should().Be(10);

        var net = QuickBooksAccountingService.BuildTermPayload(new AccountingTerms("Net 45", 45, 0m, 0));
        net.Should().NotContainKey("DiscountPercent");
        net["DueDays"].Should().Be(45);
    }

    [Fact]
    public void Long_term_names_are_cut_to_what_QuickBooks_stores_and_looked_up_the_same_way()
    {
        var terms = new AccountingTerms("1.5/15 Net 45 for distribution partners", 45, 1.5m, 15);

        var name = QuickBooksAccountingService.TermName(terms);

        name.Should().Be("1.5/15 Net 45 for distribution");
        QuickBooksAccountingService.BuildTermPayload(terms)["Name"].Should().Be(name);
        QuickBooksAccountingService.TermName(new AccountingTerms("Net 30", 30, 0m, 0)).Should().Be("Net 30");
    }
}
