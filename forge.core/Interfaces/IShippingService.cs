using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IShippingService
{
    Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct);
    Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct);
    Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);
}
