namespace Forge.Core.Models;

public record CreateLotRecordRequestModel(
    string? LotNumber,
    int PartId,
    int? JobId,
    int? ProductionRunId,
    int? PurchaseOrderLineId,
    decimal Quantity,
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    string? Notes);
