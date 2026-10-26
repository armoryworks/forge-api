namespace Forge.Api.Features.Shipments;

/// <summary>The tariff classification and country of origin printed for one shipment line.</summary>
public record ShipmentCustomsLine(string? HtsCode, string? CountryOfOrigin);
