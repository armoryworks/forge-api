namespace Forge.Core.Models;

public record LotRecordResponseModel(
    int Id,
    string LotNumber,
    int PartId,
    string PartNumber,
    string? PartDescription,
    int? JobId,
    string? JobNumber,
    int? ProductionRunId,
    int? PurchaseOrderLineId,
    decimal Quantity,
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    string? Notes,
    DateTimeOffset CreatedAt);
