namespace Forge.Core.Models;

/// <summary>An open purchase-order line for the affected part with quantity still to receive.</summary>
public record EcoImpactPurchaseOrderLineModel(
    int PurchaseOrderId,
    string PoNumber,
    int LineId,
    string VendorName,
    decimal OpenQuantity,
    string Status);
