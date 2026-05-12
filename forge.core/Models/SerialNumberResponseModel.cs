using Forge.Core.Enums;

namespace Forge.Core.Models;

public record SerialNumberResponseModel(
    int Id,
    int PartId,
    string PartNumber,
    string SerialValue,
    SerialNumberStatus Status,
    int? JobId,
    string? JobNumber,
    int? LotRecordId,
    string? LotNumber,
    int? CurrentLocationId,
    string? CurrentLocationName,
    int? ShipmentLineId,
    int? CustomerId,
    string? CustomerName,
    int? ParentSerialId,
    string? ParentSerialValue,
    DateTimeOffset? ManufacturedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ScrappedAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    int ChildCount);
