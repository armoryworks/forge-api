namespace Forge.Core.Models;

public record InvoiceSourceShipmentLineResponseModel(
    int? PartId,
    string? PartNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
