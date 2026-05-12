namespace Forge.Core.Models;

public record ScanContextResponseModel(
    int PartId,
    string PartNumber,
    string? Description,
    decimal CurrentStock,
    string? CurrentLocationName,
    int? CurrentLocationId,
    List<ScanAvailableAction> AvailableActions);

public record ScanAvailableAction(
    string Action,
    bool Enabled,
    string? DisabledReason,
    object? Context);

public record ScanMoveRequestModel(int PartId, int FromLocationId, int ToLocationId, decimal Quantity);

public record ScanCountRequestModel(int PartId, int LocationId, decimal ActualCount);

public record ScanReceiveRequestModel(int PartId, int PurchaseOrderLineId, decimal Quantity, int ToLocationId);

public record ScanShipRequestModel(int PartId, int ShipmentLineId, decimal Quantity);

public record ScanIssueRequestModel(int PartId, int JobId, decimal Quantity, int FromLocationId);

public record ScanReversalRequestModel(int ScanActionLogId, string Pin);

public record ScanLogEntryModel(
    int Id,
    string ActionType,
    string UserName,
    string? PartNumber,
    decimal Quantity,
    string? FromLocation,
    string? ToLocation,
    string? RelatedEntity,
    bool IsReversed,
    DateTimeOffset CreatedAt);

public record ScanDeviceRequestModel(string DeviceId, string? DeviceName);

public record ScanDeviceResponseModel(
    int Id,
    string DeviceId,
    string? DeviceName,
    DateTimeOffset PairedAt,
    bool IsActive);
