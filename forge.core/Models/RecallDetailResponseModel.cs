using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Full recall snapshot: header rollups + the frozen affected-lot and affected-shipment sets.</summary>
public record RecallDetailResponseModel(
    int Id,
    int InitiatedLotId,
    string InitiatedLotNumber,
    string Reason,
    DateTimeOffset RecallDate,
    RecallStatus Status,
    int AffectedLotsCount,
    int AffectedShipmentsCount,
    decimal TotalQuarantinedQuantity,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNotes,
    List<RecallAffectedLotModel> AffectedLots,
    List<RecallAffectedShipmentModel> AffectedShipments,
    DateTimeOffset CreatedAt);

/// <summary>An affected lot in a recall snapshot (the recalled lot or a downstream produced lot).</summary>
public record RecallAffectedLotModel(
    int LotId,
    string LotNumber,
    string PartNumber,
    decimal ConsumedQuantity,
    int? JobId,
    decimal OnHandQuantity,
    decimal QuarantinedQuantity);

/// <summary>A shipment (+ customer) that carried an affected lot.</summary>
public record RecallAffectedShipmentModel(
    int ShipmentId,
    string ShipmentNumber,
    int CustomerId,
    string CustomerName,
    decimal AffectedQuantity,
    DateTimeOffset? ShippedDate,
    string? TrackingNumber);
