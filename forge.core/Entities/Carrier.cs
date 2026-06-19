using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// A shipper the install can select on a shipment — either a real integrated carrier
/// (UPS/FedEx/USPS/DHL) or a custom "shadow" carrier the user defines (a house courier, a freight
/// broker, will-call). Master data: it describes what the carrier IS. The tracking number and the
/// per-shipment selection live on the <see cref="Shipment"/>. When <see cref="RequiresScanToShip"/>
/// is set, a shipment assigned to this carrier can only be marked Shipped after the printed Forge
/// label QR (the shipment's coverage-bound ScanCode) is scanned.
/// </summary>
public class Carrier : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Short stable code (e.g. "UPS", "HOUSE"). Optional; unique when present.</summary>
    public string? Code { get; set; }

    /// <summary>Standard Carrier Alpha Code, when the carrier has one (used on the BOL / freight).</summary>
    public string? Scac { get; set; }

    public CarrierIntegrationKind IntegrationKind { get; set; } = CarrierIntegrationKind.Manual;
    public CarrierDeliveryUpdateMode DeliveryUpdateMode { get; set; } = CarrierDeliveryUpdateMode.Manual;

    /// <summary>
    /// Bridges an <see cref="CarrierIntegrationKind.Api"/> carrier to the IShippingCarrierService id
    /// ("ups", "fedex", "usps", "dhl") used by the rate/label/tracking integration layer. Null for
    /// manual / custom carriers.
    /// </summary>
    public string? IntegrationServiceId { get; set; }

    /// <summary>
    /// When true (the default for a set-up carrier), the worker must scan the shipment's printed
    /// Forge label QR before the shipment can flip to Shipped. Lets a process opt a carrier out.
    /// </summary>
    public bool RequiresScanToShip { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? Notes { get; set; }

    // ── Live-integration credentials (entered via the carrier admin UI; Api carriers only).
    /// <summary>The carrier API key / client id / consumer key — an identifier, stored as-is.</summary>
    public string? CredentialClientId { get; set; }

    /// <summary>
    /// The carrier API secret, stored ENCRYPTED at rest (ITokenEncryptionService) and never returned by
    /// the API — write-only from the UI's perspective. Null until credentials are entered.
    /// </summary>
    public string? CredentialSecret { get; set; }

    /// <summary>The carrier account / shipper number (identifier; semi-sensitive, stored as-is).</summary>
    public string? CredentialAccountNumber { get; set; }

    /// <summary>"sandbox" or "production" — selects the carrier API host for this carrier's credentials.</summary>
    public string? CredentialEnvironment { get; set; }
}
