namespace Forge.Core.Models;

public record AddPurchaseOrderLineRequestModel(
    int? PartId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes,
    int? PurchaseUnitId = null,
    string? ManualOverrideReason = null);
