using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integrations.Shipping;

/// <summary>
/// Contract tests for the FedEx adapter — drives a real <see cref="FedExShippingService"/> against canned
/// FedEx-shaped responses (token / rate / ship / track) via the shared route-aware fake, verifying request
/// building + response PARSING with no FedEx sandbox. Same harness as the UPS tests (carrier-by-carrier).
/// </summary>
public class FedExShippingServiceContractTests
{
    private const string TokenJson = """{"access_token":"tok-123","token_type":"bearer","expires_in":3600}""";

    private const string RateJson = """
    {"output":{"rateReplyDetails":[
      {"serviceType":"FEDEX_GROUND","transit":{"transitDays":"THREE_DAYS"},"ratedShipmentDetails":[{"totalNetCharge":12.34}]},
      {"serviceType":"FEDEX_2_DAY","transit":{"transitDays":"TWO_DAYS"},"ratedShipmentDetails":[{"totalNetCharge":24.50}]}
    ]}}
    """;

    private const string ShipJson = """
    {"output":{"transactionShipments":[{"masterTrackingNumber":"794698123456",
      "pieceResponses":[{"trackingNumber":"794698123456","packageDocuments":[{"url":"https://fedex.example/label/794698123456.png"}]}]}]}}
    """;

    private const string TrackDeliveredJson = """
    {"output":{"completeTrackResults":[{"trackResults":[{
      "latestStatusDetail":{"code":"DL","description":"Delivered"},
      "estimatedDeliveryTimeWindow":{"window":{"ends":"2026-06-20T17:00:00Z"}},
      "scanEvents":[
        {"date":"2026-06-20T10:15:00Z","eventDescription":"Delivered","scanLocation":{"city":"Atlanta","stateOrProvinceCode":"GA"}},
        {"date":"2026-06-19T20:00:00Z","eventDescription":"At local FedEx facility","scanLocation":{"city":"Memphis"}}
      ]}]}]}}
    """;

    private static ShipmentRequest SampleRequest() => new(
        new ShippingAddress("Acme Mfg", "1 Main St", "Memphis", "TN", "38116", "US"),
        new ShippingAddress("Customer Co", "2 Oak Ave", "Atlanta", "GA", "30301", "US"),
        [new ShippingPackage(2m, 10m, 8m, 6m)],
        null);

    private static FedExShippingService MakeService(Dictionary<string, string> routes)
    {
        var opts = Options.Create(new FedExOptions
        {
            ClientId = "cid",
            ClientSecret = "csec",
            AccountNumber = "A1234",
            Environment = "sandbox",
        });
        return new FedExShippingService(
            new StubHttpClientFactory(new RouteStubHandler(routes)), opts, NullLogger<FedExShippingService>.Instance);
    }

    [Fact]
    public async Task GetRates_parses_rate_reply_details()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/rate/v1/"] = RateJson });

        var rates = await svc.GetRatesAsync(SampleRequest(), CancellationToken.None);

        rates.Should().HaveCount(2);
        rates.Should().ContainSingle(r => r.CarrierId == "fedex-fedex_ground" && r.ServiceName == "Ground"
            && r.Price == 12.34m && r.EstimatedDays == 3);
        rates.Should().ContainSingle(r => r.CarrierId == "fedex-fedex_2_day" && r.ServiceName == "2 Day"
            && r.Price == 24.50m && r.EstimatedDays == 2);
    }

    [Fact]
    public async Task CreateLabel_parses_master_tracking_and_label_url()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/ship/v1/"] = ShipJson });

        var label = await svc.CreateLabelAsync(SampleRequest(), "fedex-ground", CancellationToken.None);

        label.TrackingNumber.Should().Be("794698123456");
        label.LabelUrl.Should().Be("https://fedex.example/label/794698123456.png");
        label.CarrierName.Should().Be("FedEx");
    }

    [Fact]
    public async Task GetTracking_parses_delivered_status_and_scan_events()
    {
        var svc = MakeService(new() { ["/oauth/token"] = TokenJson, ["/track/v1/"] = TrackDeliveredJson });

        var tracking = await svc.GetTrackingAsync("794698123456", CancellationToken.None);

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
        var svc = new FedExShippingService(
            new StubHttpClientFactory(new RouteStubHandler(new Dictionary<string, string>())),
            Options.Create(new FedExOptions()), // no creds
            NullLogger<FedExShippingService>.Instance);

        svc.IsConfigured.Should().BeFalse();
        (await svc.GetRatesAsync(SampleRequest(), CancellationToken.None)).Should().BeEmpty();
        (await svc.GetTrackingAsync("794698123456", CancellationToken.None)).Should().BeNull();
    }
}
