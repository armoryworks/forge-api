namespace Forge.Core.Models;

public record LotTraceabilityResponseModel(
    string LotNumber,
    string PartNumber,
    string? PartDescription,
    List<LotTraceJobModel> Jobs,
    List<LotTraceProductionRunModel> ProductionRuns,
    List<LotTracePurchaseOrderModel> PurchaseOrders,
    List<LotTraceBinLocationModel> BinLocations,
    List<LotTraceInspectionModel> Inspections);
