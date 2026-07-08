namespace Forge.Core.Models;

/// <summary>
/// Read model for one stage of a staged production/shipment/payment plan on a
/// sales order. Stages are the user-owned editable layer; the derived
/// backward-scheduling timeline (see <see cref="SalesOrderStagesResponseModel.DerivedTimeline"/>)
/// stays advisory.
/// </summary>
public record SalesOrderStageResponseModel(
    int Id,
    int Sequence,
    string Name,
    string Status,
    DateTimeOffset? PlannedProductionComplete,
    DateTimeOffset? PlannedShipDate,
    DateTimeOffset? ActualShipDate,
    int? ShipmentId,
    string? ShipmentNumber,
    int? PaymentMilestoneId,
    string? PaymentMilestoneName,
    string? Notes,
    List<SalesOrderStageLineResponseModel> Lines,
    List<SalesOrderStageLotResponseModel> Lots);
