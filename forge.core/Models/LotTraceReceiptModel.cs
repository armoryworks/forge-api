namespace Forge.Core.Models;

public record LotTraceReceiptModel(
    int ReceivingRecordId,
    string? ReceiptNumber,
    int PurchaseOrderId,
    string PoNumber,
    int VendorId,
    string VendorName,
    DateTimeOffset ReceivedAt,
    decimal Quantity,
    string InspectionStatus);
