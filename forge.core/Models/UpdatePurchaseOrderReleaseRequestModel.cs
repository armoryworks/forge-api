
namespace Forge.Core.Models;

public record UpdatePurchaseOrderReleaseRequestModel
{
    public decimal? Quantity { get; init; }
    public DateTimeOffset? RequestedDeliveryDate { get; init; }
    public DateTimeOffset? ActualDeliveryDate { get; init; }
    public PurchaseOrderReleaseStatus? Status { get; init; }
    public string? Notes { get; init; }
}
