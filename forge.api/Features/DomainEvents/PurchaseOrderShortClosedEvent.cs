using MediatR;

namespace Forge.Api.Features.DomainEvents;

// Phase 3 / WU-14 / H3 — emitted when a partially-received PO is short-closed.
// Audit pickup is via the WU-03 audit-writer infrastructure (see existing
// pattern with PurchaseOrderReceivedEvent et al).
public record PurchaseOrderShortClosedEvent(
    int PurchaseOrderId,
    int UserId,
    string Reason,
    decimal CancelledQuantity) : INotification;
