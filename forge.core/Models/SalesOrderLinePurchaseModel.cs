namespace Forge.Core.Models;

/// <summary>A purchase order line bought for a sales order line, drop-ship or back-to-back.</summary>
public record SalesOrderLinePurchaseModel(
    int PurchaseOrderId,
    string PONumber,
    int PurchaseOrderLineId,
    string FulfilmentType,
    string Status,
    string VendorName,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal RemainingQuantity,
    DateTimeOffset? ExpectedDeliveryDate);
