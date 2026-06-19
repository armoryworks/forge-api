using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integrations.Shipping;

/// <summary>
/// Contract tests for the DHL Express (MyDHL API) adapter against canned responses. DHL uses Basic auth
/// (no token endpoint), numeric prices, and ISO-8601 dates. Same harness as UPS/FedEx/USPS.
/// </summary>
public class DhlShippingServiceContractTests
{
    private const string RateJson = """
    {"products":[
      {"productCode":"P","productName":"EXPRESS WORLDWIDE",
       "totalPrice":[{"currencyType":"BILLC","price":45.20,"priceCurrency":"USD"}]}
    ]}
    """;

    private const string LabelJson = """
    {"shipmentTrackingNumber":"1234567890","documents":[{"typeCode":"label","content":"JVBERi0xLjQK"}]}
    """;

    private const string TrackDeliveredJson = """
    {"shipments":[{
      "status":{"statusCode":"delivered","description":"Delivered"},
      "estimatedTimeOfDelivery":"2026-06-20T17:00:00Z",
      "events":[
        {"timestamp":"2026-06-20T10:15:00Z","description":"Delivered","location":{"address":{"addressLocality":"Atlanta"}}},
        {"timestamp":"2026-06-19T20:00:00Z","description":"Processed at facility","location":{"address":{"addressLocality":"Leipzig"}}}
      ]}]}
    """;

    private static ShipmentRequest SampleRequest() => new(
        new ShippingAddress("Acme Mfg", "1 Main St", "Memphis", "TN", "38116", "US"),
        new ShippingAddress("Customer Co", "2 Oak Ave", "Atlanta", "GA", "30301", "US"),
        [new ShippingPackage(2m, 10m, 8m, 6m)],
        null);

    private static DhlShippingService MakeService(Dictionary<string, string> routes)
    {
        var opts = Options.Create(new DhlOptions { ApiKey = "key", ApiSecret = "secret", AccountNumber = "A1234" });
        return new DhlShippingService(
            new StubHttpClientFactory(new RouteStubHandler(routes)), opts, NullLogger<DhlShippingService>.Instance);
    }

    [Fact]
    public async Task GetRates_parses_products_and_billc_price()
    {
        var svc = MakeService(new() { ["/rates"] = RateJson });

        var rates = await svc.GetRatesAsync(SampleRequest(), CancellationToken.None);

        rates.Should().ContainSingle();
        rates[0].CarrierId.Should().Be("dhl-p");
        rates[0].ServiceName.Should().Be("EXPRESS WORLDWIDE");
        rates[0].Price.Should().Be(45.20m);
    }

    [Fact]
    public async Task CreateLabel_parses_tracking_and_document()
    {
        // Label POST hits /shipments (the tracking GET is /shipments/{n}/tracking — different route key).
        var svc = MakeService(new() { ["/shipments"] = LabelJson });

        var label = await svc.CreateLabelAsync(SampleRequest(), "dhl-p", CancellationToken.None);

        label.TrackingNumber.Should().Be("1234567890");
        label.LabelUrl.Should().StartWith("data:application/pdf;base64,");
        label.CarrierName.Should().Be("DHL Express");
    }

    [Fact]
    public async Task GetTracking_parses_delivered_status_and_events()
    {
        var svc = MakeService(new() { ["/tracking"] = TrackDeliveredJson });

        var tracking = await svc.GetTrackingAsync("1234567890", CancellationToken.None);

        tracking.Should().NotBeNull();
        tracking!.Status.Should().Be("Delivered");
        ShipmentDeliveryStatus.IsDelivered(tracking.Status).Should().BeTrue();
        tracking.Events.Should().HaveCount(2);
        tracking.Events[0].Location.Should().Be("Atlanta");
        tracking.Events[0].Description.Should().Be("Delivered");
        tracking.EstimatedDelivery.Should().NotBeNull();
    }

    [Fact]
    public async Task Unconfigured_service_is_inert()
    {
        var svc = new DhlShippingService(
            new StubHttpClientFactory(new RouteStubHandler(new Dictionary<string, string>())),
            Options.Create(new DhlOptions()),
            NullLogger<DhlShippingService>.Instance);

        svc.IsConfigured.Should().BeFalse();
        (await svc.GetRatesAsync(SampleRequest(), CancellationToken.None)).Should().BeEmpty();
        (await svc.GetTrackingAsync("1234567890", CancellationToken.None)).Should().BeNull();
    }
}
