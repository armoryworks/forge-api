using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Integrations.Shipping;

/// <summary>
/// Contract tests for the USPS adapter against canned USPS v3 (api.usps.com) responses. Notably locks the
/// fixed tracking parser to the MODERN Tracking 3.0 shape (camelCase trackingEvents[]) rather than the
/// retired Web Tools PascalCase TrackSummary it used to read. Same harness as UPS/FedEx.
/// </summary>
public class UspsShippingServiceContractTests
{
    private const string TokenJson = """{"access_token":"tok-123","token_type":"Bearer","expires_in":3600}""";

    private const string RateJson = """
    {"totalRates":[
      {"mailClass":"PRIORITY_MAIL","description":"Priority Mail","totalBasePrice":8.90,"commitment":{"name":"2-Day"}},
      {"mailClass":"PARCEL_SELECT","description":"Parcel Select","totalBasePrice":6.50,"commitment":{"name":"3-Day"}}
    ]}
    """;

    private const string LabelJson = """{"trackingNumber":"9400100000000000000000","labelImage":"JVBERi0xLjQK"}""";

    private const string TrackDeliveredJson = """
    {"trackingNumber":"9400100000000000000000",
     "status":"Delivered",
     "statusSummary":"Your item was delivered in or at the mailbox at 10:15 am on June 20.",
     "expectedDeliveryTimeStamp":"2026-06-20T17:00:00Z",
     "trackingEvents":[
       {"eventType":"DELIVERED","eventDescription":"Delivered, In/At Mailbox","eventCity":"Atlanta","eventState":"GA","eventTimestamp":"2026-06-20T10:15:00Z"},
       {"eventType":"DEPARTED","eventDescription":"Departed USPS Regional Facility","eventCity":"Memphis","eventTimestamp":"2026-06-19T20:00:00Z"}
     ]}
    """;

    private static ShipmentRequest SampleRequest() => new(
        new ShippingAddress("Acme Mfg", "1 Main St", "Memphis", "TN", "38116", "US"),
        new ShippingAddress("Customer Co", "2 Oak Ave", "Atlanta", "GA", "30301", "US"),
        [new ShippingPackage(2m, 10m, 8m, 6m)],
        null);

    private static UspsShippingService MakeService(Dictionary<string, string> routes)
    {
        var opts = Options.Create(new UspsOptions { ConsumerKey = "ck", ConsumerSecret = "cs" });
        return new UspsShippingService(
            new StubHttpClientFactory(new RouteStubHandler(routes)), opts, NullLogger<UspsShippingService>.Instance);
    }

    [Fact]
    public async Task GetRates_parses_total_rates()
    {
        var svc = MakeService(new() { ["/oauth2/v3/token"] = TokenJson, ["/prices/v3/"] = RateJson });

        var rates = await svc.GetRatesAsync(SampleRequest(), CancellationToken.None);

        rates.Should().HaveCount(2);
        rates.Should().ContainSingle(r => r.ServiceName == "Priority Mail" && r.Price == 8.90m && r.EstimatedDays == 2);
        rates.Should().ContainSingle(r => r.ServiceName == "Parcel Select" && r.Price == 6.50m && r.EstimatedDays == 3);
    }

    [Fact]
    public async Task CreateLabel_parses_tracking_and_label_image()
    {
        var svc = MakeService(new() { ["/oauth2/v3/token"] = TokenJson, ["/labels/v3/"] = LabelJson });

        var label = await svc.CreateLabelAsync(SampleRequest(), "usps-priority_mail", CancellationToken.None);

        label.TrackingNumber.Should().Be("9400100000000000000000");
        label.LabelUrl.Should().StartWith("data:application/pdf;base64,");
        label.CarrierName.Should().Be("USPS");
    }

    [Fact]
    public async Task GetTracking_parses_delivered_status_and_modern_events()
    {
        var svc = MakeService(new() { ["/oauth2/v3/token"] = TokenJson, ["/tracking/v3/"] = TrackDeliveredJson });

        var tracking = await svc.GetTrackingAsync("9400100000000000000000", CancellationToken.None);

        tracking.Should().NotBeNull();
        ShipmentDeliveryStatus.IsDelivered(tracking!.Status).Should().BeTrue();
        // The fix: events now parse from trackingEvents[] (were silently empty under the legacy shape).
        tracking.Events.Should().HaveCount(2);
        tracking.Events[0].Location.Should().Be("Atlanta");
        tracking.Events[0].Description.Should().Be("Delivered, In/At Mailbox");
        tracking.EstimatedDelivery.Should().NotBeNull();
    }

    [Fact]
    public async Task Unconfigured_service_is_inert()
    {
        var svc = new UspsShippingService(
            new StubHttpClientFactory(new RouteStubHandler(new Dictionary<string, string>())),
            Options.Create(new UspsOptions()),
            NullLogger<UspsShippingService>.Instance);

        svc.IsConfigured.Should().BeFalse();
        (await svc.GetRatesAsync(SampleRequest(), CancellationToken.None)).Should().BeEmpty();
        (await svc.GetTrackingAsync("9400", CancellationToken.None)).Should().BeNull();
    }
}
