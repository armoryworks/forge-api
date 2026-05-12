using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// Backward-from-part PO history row. One per (PO, line) — same PO with the
/// part on multiple lines (different quantities, different unit prices)
/// shows multiple rows, which is what buyers want to see.
/// </summary>
/// <param name="PurchaseOrderId">PO header id — used to deep-link the PO #.</param>
/// <param name="PurchaseOrderLineId">Line id — stable row key.</param>
/// <param name="PoNumber">Human-readable PO number (e.g. "PO-2026-0042").</param>
/// <param name="VendorId">Vendor FK — used to deep-link the vendor name.</param>
/// <param name="VendorName">Display name of the vendor at the time of query.</param>
/// <param name="Status">PO status snapshot (Draft / Submitted / Acknowledged / Closed / etc.).</param>
/// <param name="OrderedQuantity">Line-level ordered quantity (decimal — UoM-aware).</param>
/// <param name="ReceivedQuantity">Line-level received quantity to date.</param>
/// <param name="UnitPrice">Per-unit price agreed on the line.</param>
/// <param name="LineTotal">OrderedQuantity * UnitPrice — convenience for display.</param>
/// <param name="OrderedDate">Most-meaningful "when" — SubmittedDate falls back to CreatedAt for drafts.</param>
/// <param name="ExpectedDeliveryDate">Optional — null on draft / blanket / where the buyer hasn't set one.</param>
public record PartPurchaseHistoryItemResponseModel(
    int PurchaseOrderId,
    int PurchaseOrderLineId,
    string PoNumber,
    int VendorId,
    string VendorName,
    PurchaseOrderStatus Status,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal UnitPrice,
    decimal LineTotal,
    DateTimeOffset OrderedDate,
    DateTimeOffset? ExpectedDeliveryDate);
