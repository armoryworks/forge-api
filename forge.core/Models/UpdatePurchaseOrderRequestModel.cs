namespace Forge.Core.Models;

public record UpdatePurchaseOrderRequestModel(
    string? Notes,
    DateTimeOffset? ExpectedDeliveryDate,
    // Optional caller-supplied PO number — editable in Draft only, gated by
    // purchase_orders.allow_manual_numbers.
    string? PONumber = null);
