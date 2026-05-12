namespace Forge.Core.Models;

// Phase 3 / WU-14 / H3 — short-close a partially-received PO. Reason is
// required so AP / Procurement has an audit trail of why the remaining
// quantity won't be received (vendor backorder cancelled, item
// discontinued, etc.).
public record ShortClosePurchaseOrderRequestModel(string Reason);
