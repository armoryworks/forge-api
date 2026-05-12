namespace Forge.Core.Models;

public record BackToBackStatusResponseModel
{
    public int SalesOrderId { get; init; }
    public string SalesOrderNumber { get; init; } = string.Empty;
    public int SalesOrderLineId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public int? PurchaseOrderId { get; init; }
    public string? PurchaseOrderNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal ReceivedQuantity { get; init; }
}
