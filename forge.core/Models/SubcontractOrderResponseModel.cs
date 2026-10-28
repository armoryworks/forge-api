namespace Forge.Core.Models;

public record SubcontractOrderResponseModel(
    int Id, int JobId, string JobNumber, int OperationId, string OperationName,
    int VendorId, string VendorName, int? PurchaseOrderId, string? PoNumber,
    decimal Quantity, decimal UnitCost, decimal TotalCost,
    DateTimeOffset SentAt, DateTimeOffset? ExpectedReturnDate, DateTimeOffset? ReceivedAt,
    decimal? ReceivedQuantity, string Status,
    string? ShippingTrackingNumber, string? ReturnTrackingNumber, string? Notes,
    DateTimeOffset CreatedAt,
    decimal ScrapQuantity = 0m,
    decimal OverTolerancePct = 0m,
    decimal UnderTolerancePct = 0m,
    decimal MaxAcceptedQuantity = 0m,
    decimal MinCompleteQuantity = 0m,
    string? OverToleranceReason = null);
