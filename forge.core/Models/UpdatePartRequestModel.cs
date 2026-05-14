
namespace Forge.Core.Models;

// Pillar 4 Phase 2 — clearing convention for nullable fields:
//   - int? (FK / scalar): pass -1 to clear to null. 0 also clears for legacy
//     compatibility on a few existing fields (ToolingAssetId, PreferredVendorId).
//   - decimal?: pass a negative value (< 0) to clear to null. Existing
//     thresholds use 0 as the clear sentinel; new fields use < 0.
//   - string?: pass empty/whitespace to clear to null.
//   - enum?: cannot be cleared via this endpoint. Set to a new value or
//     leave null to mean "no change".
//   - bool?: null = no change; true/false sets the entity value explicitly.
public record UpdatePartRequestModel(
    string? Name,
    string? Description,
    string? Revision,
    PartStatus? Status,
    // Pillar 1 — Decomposed type axes. The legacy PartType / Material /
    // MoldToolRef fields were retired pre-beta; new updates set the three
    // axes (ProcurementSource / InventoryClass / ItemKindId) directly.
    ProcurementSource? ProcurementSource,
    InventoryClass? InventoryClass,
    int? ToolingAssetId,
    int? PreferredVendorId,
    decimal? MinStockThreshold,
    decimal? ReorderPoint,
    decimal? ReorderQuantity,
    int? SafetyStockDays,
    // Workflow Pattern Phase 5 — manual cost override (Tier 1 single-rate).
    // Sentinel value -1 means "clear to null".
    decimal? ManualCostOverride = null,
    // Tier 0 — replaces legacy IsSerialTracked boolean.
    TraceabilityType? TraceabilityType = null,
    // Tier 0 — cycle-counting frequency tier. Empty string clears.
    AbcClass? AbcClass = null,
    // Pillar 4 Phase 2 — UoM cluster (FK to UnitOfMeasure)
    int? StockUomId = null,
    int? PurchaseUomId = null,
    int? SalesUomId = null,
    // Pillar 4 Phase 2 — MRP cluster
    bool? IsMrpPlanned = null,
    LotSizingRule? LotSizingRule = null,
    decimal? FixedOrderQuantity = null,
    decimal? MinimumOrderQuantity = null,
    decimal? OrderMultiple = null,
    int? PlanningFenceDays = null,
    int? DemandFenceDays = null,
    // Pillar 4 Phase 2 — Quality cluster (receiving inspection)
    bool? RequiresReceivingInspection = null,
    int? ReceivingInspectionTemplateId = null,
    ReceivingInspectionFrequency? InspectionFrequency = null,
    int? InspectionSkipAfterN = null,
    // Pillar 4 Phase 2 — Material cluster (measurement profile + valuation)
    int? MaterialSpecId = null,
    decimal? WeightEach = null,
    string? WeightDisplayUnit = null,
    decimal? LengthMm = null,
    decimal? WidthMm = null,
    decimal? HeightMm = null,
    string? DimensionDisplayUnit = null,
    decimal? VolumeMl = null,
    string? VolumeDisplayUnit = null,
    int? ValuationClassId = null,
    // Pillar 4 Phase 2 — Tier 3 compliance / classification + ad-hoc fields
    string? HazmatClass = null,
    int? ShelfLifeDays = null,
    BackflushPolicy? BackflushPolicy = null,
    bool? IsKit = null,
    bool? IsConfigurable = null,
    int? DefaultBinId = null,
    int? SourcePartId = null,
    string? HtsCode = null);
