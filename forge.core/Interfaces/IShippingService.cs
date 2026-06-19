using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IShippingService
{
    Task<List<ShippingRate>> GetRatesAsync(ShipmentRequest request, CancellationToken ct);
    Task<ShippingLabel> CreateLabelAsync(ShipmentRequest request, string carrierId, CancellationToken ct);
    Task<ShipmentTracking?> GetTrackingAsync(string trackingNumber, CancellationToken ct);
    Task<bool> TestConnectionAsync(CancellationToken ct);

    // Courier pickup (additive). Default-implemented so a carrier that doesn't support it (or isn't wired
    // yet) inherits a clean NotSupported rather than forcing every adapter to change. Mock + the carriers
    // that implement pickup override these.
    Task<PickupConfirmation> SchedulePickupAsync(PickupRequest request, string carrierId, CancellationToken ct)
        => throw new NotSupportedException("Scheduling a pickup is not supported by this carrier.");

    Task<bool> CancelPickupAsync(string confirmationNumber, string carrierId, CancellationToken ct)
        => throw new NotSupportedException("Cancelling a pickup is not supported by this carrier.");
}
