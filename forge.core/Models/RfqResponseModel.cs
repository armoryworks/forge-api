namespace Forge.Core.Models;

public record RfqResponseModel(
    int Id,
    string RfqNumber,
    int PartId,
    string PartNumber,
    string PartDescription,
    decimal Quantity,
    DateTimeOffset RequiredDate,
    string Status,
    string? Description,
    string? SpecialInstructions,
    DateTimeOffset? ResponseDeadline,
    DateTimeOffset? SentAt,
    DateTimeOffset? AwardedAt,
    int? AwardedVendorResponseId,
    int? GeneratedPurchaseOrderId,
    string? Notes,
    int VendorResponseCount,
    int ReceivedResponseCount,
    DateTimeOffset CreatedAt);
