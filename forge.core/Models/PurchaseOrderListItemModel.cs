namespace Forge.Core.Models;

// Phase 3 / WU-10 / F8-partial — TotalOrdered / TotalReceived are decimal
// (aggregate of decimal line quantities, was int).
public record PurchaseOrderListItemModel(
    int Id,
    string PONumber,
    int VendorId,
    string VendorName,
    int? JobId,
    string? JobNumber,
    string Status,
    int LineCount,
    decimal TotalOrdered,
    decimal TotalReceived,
    DateTimeOffset? ExpectedDeliveryDate,
    bool IsBlanket,
    DateTimeOffset CreatedAt,
    // Bought-parts effort PR2.5 — vendor-min soft-warning surface. True when
    // the vendor's MinOrderAmount is set AND the PO total (line value +
    // EstimatedFreight) falls below it. Used by the list to render a small
    // warning chip alongside the row so the buyer sees the issue without
    // opening the PO. Re-computed in the detail handler so the two views
    // can't drift.
    bool BelowVendorMinimum = false,
    // S4b provenance — where the PO came from (PoOriginSource as string:
    // Manual / AutoMrp / AutoQuote / ExternalIntegration / Edi), the display
    // name of the creating user for Manual POs ("Last, First"), and the
    // free-text origin reference (suggestion id, RFQ number, provider name).
    string OriginSource = "Manual",
    string? OriginUserName = null,
    string? OriginReference = null);
