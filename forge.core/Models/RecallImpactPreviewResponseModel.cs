namespace Forge.Core.Models;

/// <summary>
/// What initiating a recall on a lot would record, computed without saving anything or moving stock: the
/// affected lots (the lot and every downstream produced lot), the shipments and customers that received
/// them, and the on-hand quantity that would be put on QC hold.
/// </summary>
public record RecallImpactPreviewResponseModel(
    int InitiatedLotId,
    string InitiatedLotNumber,
    int AffectedLotsCount,
    int AffectedShipmentsCount,
    int AffectedCustomersCount,
    decimal TotalQuarantineQuantity,
    List<RecallAffectedLotModel> AffectedLots,
    List<RecallAffectedShipmentModel> AffectedShipments);
