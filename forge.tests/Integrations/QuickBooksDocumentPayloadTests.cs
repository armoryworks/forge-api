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
    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
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
}
