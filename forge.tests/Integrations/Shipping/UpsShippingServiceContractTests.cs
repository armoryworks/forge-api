using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;
using Forge.Integrations;

namespace Forge.Tests.Integrations.Shipping;

/// <summary>
/// Contract tests for the UPS adapter — drives a real <see cref="UpsShippingService"/> against canned
/// UPS-shaped responses (token / rate / ship / track) via a route-aware fake handler, so request
/// building + response PARSING are verified deterministically WITHOUT the UPS CIE sandbox. This is the
/// sandbox-independent safety net for the carrier-by-carrier work; the same harness fits FedEx/USPS/DHL.
/// Covers the classic UPS traps: single-vs-array collection fields and yyyyMMdd/HHmmss dates.
/// </summary>
public class UpsShippingServiceContractTests
{
    private const string TokenJson = """{"access_token":"tok-123","expires_in":3600,"token_type":"Bearer"}""";

    private const string RateMultiJson = """
    {"RateResponse":{"Response":{"ResponseStatus":{"Code":"1","Description":"Success"}},
      "RatedShipment":[
        {"Service":{"Code":"03"},"TotalCharges":{"CurrencyCode":"USD","MonetaryValue":"12.34"},"GuaranteedDelivery":{"BusinessDaysInTransit":"3"}},
        {"Service":{"Code":"02"},"TotalCharges":{"CurrencyCode":"USD","MonetaryValue":"24.50"},"GuaranteedDelivery":{"BusinessDaysInTransit":"2"}}
      ]}}
    """;

    // RatedShipment as a bare object (UPS does this when one service qualifies) — the single-vs-array trap.
    private const string RateSingleJson = """
    {"RateResponse":{"RatedShipment":{"Service":{"Code":"03"},"TotalCharges":{"MonetaryValue":"9.99"},"GuaranteedDelivery":{"BusinessDaysInTransit":"4"}}}}
    """;

    private const string ShipJson = """
    {"ShipmentResponse":{"Response":{"ResponseStatus":{"Code":"1"}},
      "ShipmentResults":{"ShipmentIdentificationNumber":"1Z12345E0291980793",
        "PackageResults":[{"TrackingNumber":"1Z12345E0291980793","ShippingLabel":{"ImageFormat":{"Code":"PNG"},"GraphicImage":"iVBORw0KGgoAAAANSUhEUg=="}}]}}}
    """;

    private const string TrackDeliveredJson = """
    {"trackResponse":{"shipment":[{"package":[{
      "trackingNumber":"1Z12345E0291980793",
      "currentStatus":{"code":"011","description":"Delivered"},
      "deliveryDate":[{"type":"DEL","date":"20260615"}],
      "activity":[
        {"location":{"address":{"city":"Atlanta","stateProvince":"GA","countryCode":"US"}},"status":{"type":"D","code":"KB","description":"Delivered"},"date":"20260615","time":"101500"},
        {"location":{"address":{"city":"Louisville"}},"status":{"description":"Departed from Facility"},"date":"20260614","time":"200000"}
      ]}]}]}}
    """;

    private static ShipmentRequest SampleRequest() => new(
        new ShippingAddress("Acme Mfg", "1 Main St", "Louisville", "KY", "40202", "US"),
        new ShippingAddress("Customer Co", "2 Oak Ave", "Atlanta", "GA", "30301", "US"),
        [new ShippingPackage(2m, 10m, 8m, 6m)],
        null);

    private static UpsShippingService MakeService(Dictionary<string, string> routes)
    {
        var handler = new RouteStubHandler(routes);
        var opts = Options.Create(new UpsOptions
        {
            ClientId = "cid",
            ClientSecret = "csec",
            AccountNumber = "A1234",
            Environment = "sandbox",
        });
        return new UpsShippingService(new StubFactory(handler), opts, NullLogger<UpsShippingService>.Instance);
    }

    [Fact]
    public async Task GetRates_parses_multiple_rated_shipments()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/api/rating/"] = RateMultiJson });

        var rates = await svc.GetRatesAsync(SampleRequest(), CancellationToken.None);

        rates.Should().HaveCount(2);
        rates.Should().ContainSingle(r => r.CarrierId == "ups-03" && r.ServiceName == "Ground"
            && r.Price == 12.34m && r.EstimatedDays == 3);
        rates.Should().ContainSingle(r => r.CarrierId == "ups-02" && r.ServiceName == "2nd Day Air"
            && r.Price == 24.50m && r.EstimatedDays == 2);
    }

    [Fact]
    public async Task GetRates_tolerates_single_rated_shipment_object()
    {
        // The UPS single-vs-array trap: a one-service response is an object, not an array.
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/api/rating/"] = RateSingleJson });

        var rates = await svc.GetRatesAsync(SampleRequest(), CancellationToken.None);

        rates.Should().ContainSingle();
        rates[0].CarrierId.Should().Be("ups-03");
        rates[0].Price.Should().Be(9.99m);
    }

    [Fact]
    public async Task CreateLabel_parses_tracking_and_label_image()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/api/shipments/"] = ShipJson });

        var label = await svc.CreateLabelAsync(SampleRequest(), "ups-03", CancellationToken.None);

        label.TrackingNumber.Should().Be("1Z12345E0291980793");
        label.LabelUrl.Should().StartWith("data:image/png;base64,");
        label.CarrierName.Should().Be("UPS");
    }

    [Fact]
    public async Task GetTracking_parses_delivered_status_events_and_date()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/api/track/"] = TrackDeliveredJson });

        var tracking = await svc.GetTrackingAsync("1Z12345E0291980793", CancellationToken.None);

        tracking.Should().NotBeNull();
        tracking!.Status.Should().Be("Delivered");
        // Ties the adapter to the delivery automation: UPS "Delivered" → the poll job marks the shipment.
        ShipmentDeliveryStatus.IsDelivered(tracking.Status).Should().BeTrue();
        tracking.Events.Should().HaveCount(2);
        tracking.Events[0].Location.Should().Be("Atlanta");
        tracking.Events[0].Description.Should().Be("Delivered");
        // yyyyMMdd parsed (TryParse would have failed → null); the activity timestamp parses too.
        tracking.EstimatedDelivery.Should().NotBeNull();
        tracking.Events[0].Timestamp.Should().NotBe(default);
    }

    [Fact]
    public async Task Unconfigured_service_is_inert()
    {
        var svc = new UpsShippingService(
            new StubFactory(new RouteStubHandler(new Dictionary<string, string>())),
            Options.Create(new UpsOptions()), // no creds
            NullLogger<UpsShippingService>.Instance);

        svc.IsConfigured.Should().BeFalse();
        (await svc.GetRatesAsync(SampleRequest(), CancellationToken.None)).Should().BeEmpty();
        (await svc.GetTrackingAsync("1Z999", CancellationToken.None)).Should().BeNull();
    }

    // Route-aware fake: returns the canned body whose key is a substring of the request URL.
    private sealed class RouteStubHandler(IReadOnlyDictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var body = routes.FirstOrDefault(kv => url.Contains(kv.Key)).Value ?? "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
