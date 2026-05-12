namespace Forge.Core.Models;

public record SubcontractOrderResponseModel(
    int Id, int JobId, string JobNumber, int OperationId, string OperationName,
    int VendorId, string VendorName, int? PurchaseOrderId, string? PoNumber,
    decimal Quantity, decimal UnitCost, decimal TotalCost,
    DateTimeOffset SentAt, DateTimeOffset? ExpectedReturnDate, DateTimeOffset? ReceivedAt,
    decimal? ReceivedQuantity, string Status,
    string? ShippingTrackingNumber, string? ReturnTrackingNumber, string? Notes,
    DateTimeOffset CreatedAt);
