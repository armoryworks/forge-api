namespace Forge.Core.Models;

public record PurchaseOrderDetailResponseModel(
    int Id,
    string PONumber,
    int VendorId,
    string VendorName,
    int? JobId,
    string? JobNumber,
    string Status,
    DateTimeOffset? SubmittedDate,
    DateTimeOffset? AcknowledgedDate,
    DateTimeOffset? ExpectedDeliveryDate,
    DateTimeOffset? ReceivedDate,
    string? Notes,
    bool IsBlanket,
    decimal? BlanketTotalQuantity,
    decimal? BlanketReleasedQuantity,
    decimal? BlanketRemainingQuantity,
    DateTimeOffset? BlanketExpirationDate,
    decimal? AgreedUnitPrice,
    List<PurchaseOrderLineResponseModel> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Phase 3 / WU-14 / H3 — short-close audit fields.
    string? ShortCloseReason = null,
    DateTimeOffset? ShortClosedAt = null,
    // Bought-parts effort PR2 — landed cost foundation.
    string Incoterm = "FOB_Origin",
    decimal? EstimatedFreight = null,
    string QuoteCurrency = "USD",
    decimal? FxRate = null,
    string? FxRateSource = null,
    // Soft-warning surface: when true, the vendor's MinOrderAmount is set
    // and the PO total falls below it. UI shows a non-blocking banner.
    bool BelowVendorMinimum = false,
    decimal? VendorMinimumOrderAmount = null,
    // S4b provenance — where the PO came from (PoOriginSource as string),
    // the creating user's display name for Manual POs ("Last, First"), and
    // the free-text origin reference (suggestion id, RFQ number, provider).
    string OriginSource = "Manual",
    string? OriginUserName = null,
    string? OriginReference = null);
