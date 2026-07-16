namespace Forge.Core.Models;

public record LotTraceabilityResponseModel(
    string LotNumber,
    string PartNumber,
    string? PartDescription,
    List<LotTraceJobModel> Jobs,
    List<LotTraceProductionRunModel> ProductionRuns,
    List<LotTracePurchaseOrderModel> PurchaseOrders,
    List<LotTraceBinLocationModel> BinLocations,
    List<LotTraceInspectionModel> Inspections,
    decimal Quantity,
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    List<LotTraceEventModel> Events,
    // Component genealogy (regulated-parts-safety C-2): ConsumedLots are the input
    // lots that went INTO this lot (backward trace); ProducedLots are the output
    // lots this lot was consumed INTO (forward trace — the recall blast radius).
    List<LotConsumptionEdgeModel> ConsumedLots,
    List<LotConsumptionEdgeModel> ProducedLots);
