namespace Forge.Core.Models;

public record UpdatePurchaseOrderRequestModel(
    string? Notes,
    DateTimeOffset? ExpectedDeliveryDate);
