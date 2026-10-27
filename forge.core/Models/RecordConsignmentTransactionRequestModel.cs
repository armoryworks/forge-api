namespace Forge.Core.Models;

public record RecordConsignmentTransactionRequestModel
{
    public decimal Quantity { get; init; }
    public string? Notes { get; init; }
    public int? StorageLocationId { get; init; }
    public int? BinContentId { get; init; }
    public string? LotNumber { get; init; }
    public int? JobId { get; init; }
    public int? PurchaseOrderId { get; init; }
}
