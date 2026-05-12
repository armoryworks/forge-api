using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Aggregates all registered IShippingCarrierService implementations.
/// GetRatesAsync fans out to all configured carriers in parallel.
/// CreateLabelAsync routes to the specific carrier by carrierId prefix.
/// GetTrackingAsync detects carrier from tracking number format.
/// </summary>
public class MultiCarrierShippingService(
    IEnumerable<IShippingCarrierService> carriers,
    ILogger<MultiCarrierShippingService> logger) : IShippingService
{
    private readonly IReadOnlyList<IShippingCarrierService> _carriers = carriers.ToList();

    public async Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct)
    {
        var configured = _carriers.Where(c => c.IsConfigured).ToList();
        if (configured.Count == 0)
        {
            logger.LogWarning("[MultiCarrier] No shipping carriers configured — returning empty rate list");
            return [];
        }

        var tasks = configured.Select(async c =>
        {
            try
            {
                return await c.GetRatesAsync(request, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MultiCarrier] {Carrier} GetRates failed", c.CarrierName);
                return new List<ShippingRate>();
            }
        });

        var results = await Task.WhenAll(tasks);
        var allRates = results.SelectMany(r => r).OrderBy(r => r.Price).ToList();
        logger.LogInformation("[MultiCarrier] GetRates — {Count} total rate(s) from {CarrierCount} carrier(s)", allRates.Count, configured.Count);
        return allRates;
    }

    public async Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct)
    {
        var prefix = carrierId.Split('-').FirstOrDefault() ?? carrierId;
        var carrier = _carriers.FirstOrDefault(c => c.CarrierId.Equals(prefix, StringComparison.OrdinalIgnoreCase) && c.IsConfigured)
            ?? throw new InvalidOperationException(
                $"Carrier '{prefix}' is not configured or not found. Available: {string.Join(", ", _carriers.Where(c => c.IsConfigured).Select(c => c.CarrierId))}");

        return await carrier.CreateLabelAsync(request, carrierId, ct);
    }

    public Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct)
    {
        // Detect carrier by tracking number format heuristics
        var carrier = DetectCarrier(trackingNumber);
        if (carrier is not null && carrier.IsConfigured)
            return carrier.GetTrackingAsync(trackingNumber, ct);

        // Try all configured carriers in order
        return TryAllCarriersForTracking(trackingNumber, ct);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        var configured = _carriers.Where(c => c.IsConfigured).ToList();
        if (configured.Count == 0) return false;
        var results = await Task.WhenAll(configured.Select(c => c.TestConnectionAsync(ct)));
        return results.Any(r => r);
    }

    private async Task<ShipmentTracking?> TryAllCarriersForTracking(string trackingNumber, CancellationToken ct)
    {
        foreach (var carrier in _carriers.Where(c => c.IsConfigured))
        {
            try
            {
                var result = await carrier.GetTrackingAsync(trackingNumber, ct);
                if (result is not null) return result;
            }
            catch
            {
                // try next carrier
            }
        }
        return null;
    }

    private IShippingCarrierService? DetectCarrier(string trackingNumber) =>
        // UPS: 18-char string starting with 1Z
        trackingNumber.StartsWith("1Z")
            ? _carriers.FirstOrDefault(c => c.CarrierId == "ups") :
        // FedEx: 12, 15, or 20 digit number
        trackingNumber.Length is 12 or 15 or 20 && trackingNumber.All(char.IsDigit)
            ? _carriers.FirstOrDefault(c => c.CarrierId == "fedex") :
        // USPS: 20 or 22 digit number or starts with 9
        (trackingNumber.Length is 20 or 22 || trackingNumber.StartsWith("9")) && trackingNumber.All(char.IsDigit)
            ? _carriers.FirstOrDefault(c => c.CarrierId == "usps") :
        null;
}
