namespace Forge.Core.Models;

// Phase 3 / WU-10 / F8-partial — quantities are decimal (was int).
// Phase 3 / WU-14 / H3 — CancelledShortCloseQuantity surfaces the unreceived
// portion that was abandoned at short-close, so UI can render
// "5 received / 5 short-closed / 10 ordered" without a separate query.
public record PurchaseOrderLineResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string Description,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal RemainingQuantity,
    decimal CancelledShortCloseQuantity,
    // Received-but-not-yet-billed (= received − billed). The vendor-bill 3-way-match over-bill guard caps each
    // bill line at this, so the bill-against-PO form can show "billable up to X" instead of a 409 at approve.
    decimal UnbilledReceivedQuantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes,
    int? PurchaseUnitId,
    string? PurchaseUnitLabel,
    // Reason captured when the unit price was manually overridden (null otherwise).
    string? ManualOverrideReason);
