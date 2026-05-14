
namespace Forge.Core.Models;

public record PurchaseOrderReleaseResponseModel
{
    public int Id { get; init; }
    public int ReleaseNumber { get; init; }
    public int PurchaseOrderLineId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public DateTimeOffset RequestedDeliveryDate { get; init; }
    public DateTimeOffset? ActualDeliveryDate { get; init; }
    public PurchaseOrderReleaseStatus Status { get; init; }
    public int? ReceivingRecordId { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
