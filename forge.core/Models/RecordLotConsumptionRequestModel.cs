namespace Forge.Core.Models;

/// <summary>
/// Records which input lots were consumed to produce a given output lot
/// (regulated-parts-safety C-2 component genealogy). This is the write primitive
/// that populates <c>lot_consumptions</c> so CAP-QC-RECALL forward/backward trace
/// and quarantine have edges to walk. The produced lot is identified by the route.
/// </summary>
public record RecordLotConsumptionRequestModel(
    List<LotConsumptionInputModel> Consumptions,
    int? JobId = null,
    int? ProductionRunId = null);

/// <summary>One consumed input lot and the quantity of it that went into the output lot.</summary>
public record LotConsumptionInputModel(int ConsumedLotId, decimal Quantity);
