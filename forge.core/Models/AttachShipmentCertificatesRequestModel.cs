namespace Forge.Core.Models;

/// <summary>Attaches certificates of analysis to a shipment: for the one lot named, or for every lot the shipment carried when none is named.</summary>
public record AttachShipmentCertificatesRequestModel(int? LotId = null, string? Language = null);
