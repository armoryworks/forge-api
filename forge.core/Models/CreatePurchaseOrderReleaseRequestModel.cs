namespace Forge.Core.Models;

public record CreatePurchaseOrderReleaseRequestModel
{
    public int PurchaseOrderLineId { get; init; }
    public decimal Quantity { get; init; }
    public DateTimeOffset RequestedDeliveryDate { get; init; }
    public string? Notes { get; init; }
}
