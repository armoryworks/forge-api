namespace Forge.Core.Models;

public record InvoiceSourceShipmentResponseModel(
    int Id,
    string ShipmentNumber,
    int SalesOrderId,
    string SalesOrderNumber,
    DateTimeOffset? ShippedDate);
