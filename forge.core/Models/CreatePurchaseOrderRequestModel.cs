namespace Forge.Core.Models;

public record CreatePurchaseOrderRequestModel(
    int VendorId,
    int? JobId,
    string? Notes,
    List<CreatePurchaseOrderLineModel> Lines);

// Phase 3 / WU-10 / F8-partial — Quantity is decimal (was int). UoM-aware shops
// need fractional quantities — material-by-weight, by-time, by-volume.
public record CreatePurchaseOrderLineModel(
    // Null = a part-less line (a service, or material described in words rather than a Part
    // row — services shops and construction installs). Description becomes required then.
    int? PartId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes,
    // UoM purchase-units effort — which PartPurchaseUnit (size/form) is ordered. When set,
    // Quantity counts in options and UnitPrice is per option; receiving converts to base UoM.
    int? PurchaseUnitId = null,
    // Optional reason captured when a user manually overrides the suggested
    // (vendor-tier) unit price. Null when the price was not overridden.
    string? ManualOverrideReason = null);
