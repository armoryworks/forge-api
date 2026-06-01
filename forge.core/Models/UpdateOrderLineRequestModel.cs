namespace Forge.Core.Models;

/// <summary>
/// Shared payload for editing a single line on a draft Quote / Sales Order / Purchase
/// Order (BE-1 / SO-8 / P06-4). For POs, <see cref="Quantity"/> maps to OrderedQuantity.
/// </summary>
public record UpdateOrderLineRequestModel(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes,
    // UoM purchase-units effort — PO-only: which PartPurchaseUnit the line orders
    // (null = per base unit). Ignored by Quote / Sales Order line edits.
    int? PurchaseUnitId = null);
