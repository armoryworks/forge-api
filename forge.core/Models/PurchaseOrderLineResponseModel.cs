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
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes);
