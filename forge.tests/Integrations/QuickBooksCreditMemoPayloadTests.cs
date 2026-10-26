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

/// <summary>The QuickBooks CreditMemo and RefundReceipt requests Forge sends for credit memos and refunds.</summary>
public class QuickBooksCreditMemoPayloadTests
{
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (QuickBooksAccountingService Service, CapturingHandler Handler) Create(string responseJson)
    {
        var resolver = new Mock<IExternalIdentityResolver>();
        resolver
            .Setup(r => r.ResolveAsync("quickbooks", null, It.IsAny<TokenResolutionPolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedExternalIdentity("token", "quickbooks", TokenScope.Install, null, "realm-1"));
        var handler = new CapturingHandler(responseJson);
        var service = new QuickBooksAccountingService(
            resolver.Object,
            new Forge.Tests.Helpers.StubHttpClientFactory(handler),
            Options.Create(new QuickBooksOptions()),
            NullLogger<QuickBooksAccountingService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task Credit_memo_posts_to_the_creditmemo_endpoint_with_its_lines_and_customer()
    {
        var (service, handler) = Create("""{"CreditMemo":{"Id":"77"}}""");

        var id = await service.CreateCreditMemoAsync(new AccountingDocument(
            AccountingDocumentType.CreditMemo, "cust-9",
            [new AccountingLineItem("Returned widgets", 10m, 189m, null, Taxable: true)],
            "CM-000001", 2027.03m, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        id.Should().Be("77");
        var (path, body) = handler.Requests.Single();
        path.Should().EndWith("/creditmemo");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("CustomerRef").GetProperty("value").GetString().Should().Be("cust-9");
        doc.RootElement.GetProperty("DocNumber").GetString().Should().Be("CM-000001");
        doc.RootElement.GetProperty("Line")[0].GetProperty("Amount").GetDecimal().Should().Be(1890m);
    }

    [Fact]
    public void Refund_receipt_carries_the_mapped_item_bank_account_and_currency()
    {
        var payload = QuickBooksAccountingService.BuildRefundReceiptPayload(new AccountingRefund(
            "cust-9", "RF-000001", 500m, new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), "Check", "Refund of CM-000004",
            CurrencyCode: "EUR", ExchangeRate: 1.1m, ItemExternalId: "item-5", DepositAccountExternalId: "acct-35"));

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        doc.RootElement.GetProperty("DepositToAccountRef").GetProperty("value").GetString().Should().Be("acct-35");
        doc.RootElement.GetProperty("CurrencyRef").GetProperty("value").GetString().Should().Be("EUR");
        var line = doc.RootElement.GetProperty("Line")[0];
        line.GetProperty("Amount").GetDecimal().Should().Be(500m);
        line.GetProperty("SalesItemLineDetail").GetProperty("ItemRef").GetProperty("value").GetString().Should().Be("item-5");
    }
}
