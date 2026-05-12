using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockShippingService : IShippingService
{
    private readonly ILogger<MockShippingService> _logger;

    public MockShippingService(ILogger<MockShippingService> logger)
    {
        _logger = logger;
    }

    public Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        _logger.LogInformation("[MockShipping] GetRates for {PackageCount} package(s) to {City}, {State}",
            request.Packages.Count, request.ToAddress.City, request.ToAddress.State);

        var rates = new List<ShippingRate>
        {
            new("ups-ground", "UPS", "Ground", 12.50m, 5),
            new("fedex-home", "FedEx", "Home Delivery", 15.75m, 3),
            new("usps-priority", "USPS", "Priority Mail", 8.90m, 4),
        };

        return Task.FromResult(rates);
    }

    public Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        var tracking = $"MOCK-{carrierId.ToUpperInvariant()}-{Guid.NewGuid().ToString("N")[..10]}";
        var carrierName = carrierId switch
        {
            "ups-ground" => "UPS",
            "fedex-home" => "FedEx",
            "usps-priority" => "USPS",
            _ => carrierId,
        };

        _logger.LogInformation("[MockShipping] CreateLabel via {Carrier} — tracking {Tracking}", carrierName, tracking);

        var label = new ShippingLabel(tracking, $"mock:///labels/{tracking}.pdf", carrierName);
        return Task.FromResult(label);
    }

    public Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        _logger.LogInformation("[MockShipping] GetTracking({TrackingNumber})", trackingNumber);

        var tracking = new ShipmentTracking(
            trackingNumber,
            "In Transit",
            DateTimeOffset.UtcNow.AddDays(3),
            [
                new TrackingEvent(DateTimeOffset.UtcNow.AddDays(-1), "Origin Facility", "Package picked up"),
                new TrackingEvent(DateTimeOffset.UtcNow, "Distribution Center", "In transit to destination"),
            ]);

        return Task.FromResult<ShipmentTracking?>(tracking);
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        _logger.LogInformation("[MockShipping] TestConnection — returning true");
        return Task.FromResult(true);
    }
}
