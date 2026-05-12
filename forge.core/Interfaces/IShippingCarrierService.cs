namespace Forge.Core.Interfaces;

/// <summary>
/// Extends IShippingService with carrier identity. Implementations represent individual
/// carriers (UPS, FedEx, USPS, DHL). MultiCarrierShippingService aggregates them.
/// </summary>
public interface IShippingCarrierService : IShippingService
{
    string CarrierId { get; }
    string CarrierName { get; }
    bool IsConfigured { get; }
}
