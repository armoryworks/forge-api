namespace Forge.Api.Services;

/// <summary>A shipment line that cannot be relieved: the part, the quantity it needs and the free stock there is.</summary>
public record ShortStockLine(int ShipmentLineId, int PartId, string PartNumber, decimal Needed, decimal Free);
