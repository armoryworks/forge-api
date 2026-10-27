namespace Forge.Core.Models;

public record ReservableSalesOrderLineResponseModel(
    int SalesOrderLineId,
    int SalesOrderId,
    string SalesOrderNumber,
    int LineNumber,
    string CustomerName,
    decimal OpenQuantity,
    decimal ReservedQuantity,
    DateTimeOffset? RequestedDeliveryDate);
