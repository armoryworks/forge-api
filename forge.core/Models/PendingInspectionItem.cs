namespace Forge.Core.Models;

public record PendingInspectionItem(
    int ReceivingRecordId,
    string PartNumber,
    string PartDescription,
    string PoNumber,
    string VendorName,
    decimal ReceivedQuantity,
    DateTimeOffset ReceivedAt,
    string? QcTemplateName,
    int DaysWaiting);
