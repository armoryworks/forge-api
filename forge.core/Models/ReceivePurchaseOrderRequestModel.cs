namespace Forge.Core.Models;

public record ReceivePurchaseOrderRequestModel(
    int PurchaseOrderLineId,
    decimal QuantityReceived,
    int? LocationId,
    string? LotNumber,
    string? Notes);
