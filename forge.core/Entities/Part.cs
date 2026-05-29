using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Core.Entities;

public class Part : BaseAuditableEntity, IActiveAware
{
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>
    /// Short human-readable identifier (e.g., "Sheath Mudkipper"). Required —
    /// the canonical "what is this thing" label rendered on cards, lists, and
    /// the detail-page heading. Backfilled from <see cref="Description"/> by
    /// migration <c>Add_Name_To_Part</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Long-form notes (paragraph-length detail). Optional — fill in only when
    /// there is something meaningful beyond the short Name. Used to be
    /// double-duty as both name and description; that role moved to
    /// <see cref="Name"/>.
    /// </summary>
    public string? Description { get; set; }

    public string Revision { get; set; } = "A";
    public PartStatus Status { get; set; } = PartStatus.Active;

    /// <summary>
    /// Pillar 1 — How the part is sourced (Make / Buy / Subcontract / Phantom).
    /// </summary>
    public ProcurementSource ProcurementSource { get; set; } = ProcurementSource.Buy;

    /// <summary>
    /// Pillar 1 — Which inventory bucket the part lives in (Raw / Component /
    /// Subassembly / FinishedGood / Consumable / Tool).
    /// </summary>
    public InventoryClass InventoryClass { get; set; } = InventoryClass.Component;

    /// <summary>
    /// Pillar 1 — Descriptive admin-configurable taxonomy (Fastener,
    /// Electronic, Packaging, Hardware, Material, etc.). FK to
    /// <c>reference_data</c> with group_code = 'part.item_kind'.
    /// </summary>
    public int? ItemKindId { get; set; }
    public ReferenceData? ItemKind { get; set; }

    /// <summary>
    /// Tier 0 — Replaces the legacy <c>IsSerialTracked</c> boolean.
    /// Lot tracking is now expressible.
    /// </summary>
    public TraceabilityType TraceabilityType { get; set; } = TraceabilityType.None;

    /// <summary>
    /// Tier 0 — Cycle-counting frequency tier and stock-movement KPI bucket.
    /// </summary>
    public AbcClass? AbcClass { get; set; }

    /// <summary>
    /// Pillar 2 — Material specification reference. Replaces the legacy
    /// free-text <c>Material</c> string. FK to <c>reference_data</c> with
    /// group_code = 'part.material_spec'. See
    /// <c>phase-4-output/part-type-field-relevance.md</c> § 8 (Tier 2).
    /// </summary>
    public int? MaterialSpecId { get; set; }
    public ReferenceData? MaterialSpec { get; set; }

    // ─── Pillar 2 / Tier 2 — Measurement profile + valuation ───
    // See phase-4-output/part-type-field-relevance.md § 8 (Tier 2). All
    // canonical magnitudes are stored in SI units (g, mm, mL); the matching
    // *DisplayUnit column captures the unit the user originally typed in,
    // so the UI can round-trip the same string the user entered.

    /// <summary>
    /// Mass per unit, in grams (canonical SI). Drives shipping-rate quoting
    /// and weight-based capacity checks.
    /// </summary>
    public decimal? WeightEach { get; set; }

    /// <summary>
    /// Display unit the user typed in for <see cref="WeightEach"/> — one of
    /// "g", "kg", "lb", "oz". Null = the stored value is in g and the UI
    /// may pick a default.
    /// </summary>
    public string? WeightDisplayUnit { get; set; }

    /// <summary>Primary dimension, mm.</summary>
    public decimal? LengthMm { get; set; }

    /// <summary>Secondary dimension, mm.</summary>
    public decimal? WidthMm { get; set; }

    /// <summary>Tertiary dimension, mm.</summary>
    public decimal? HeightMm { get; set; }

    /// <summary>
    /// Display unit the user typed in for the dimension columns — one of
    /// "mm", "cm", "m", "in", "ft".
    /// </summary>
    public string? DimensionDisplayUnit { get; set; }

    /// <summary>Bulk volume per unit, mL (canonical SI).</summary>
    public decimal? VolumeMl { get; set; }

    /// <summary>
    /// Display unit the user typed in for <see cref="VolumeMl"/> — one of
    /// "mL", "L", "gal".
    /// </summary>
    public string? VolumeDisplayUnit { get; set; }

    /// <summary>
    /// Pillar 2 — Inventory valuation class (FIFO / LIFO / Average /
    /// Standard etc.). FK to <c>reference_data</c>, group_code =
    /// 'part.valuation_class'. Gated behind <c>CAP-ACCT-BUILTIN</c>
    /// capability — admin UI to set this should later check the capability,
    /// but the column itself is unconditional. See
    /// <c>phase-4-output/part-type-field-relevance.md</c> § 8 (Tier 2).
    /// </summary>
    public int? ValuationClassId { get; set; }
    public ReferenceData? ValuationClass { get; set; }

    // ─── Pillar 2 / Tier 3 — Compliance + classification ───
    // See phase-4-output/part-type-field-relevance.md § 8 (Tier 3).
    // Gated by capability CAP-MD-PART-COMPLIANCE for UI exposure; the
    // columns themselves exist unconditionally.

    /// <summary>
    /// Default tariff classification (Harmonized Tariff Schedule code) for
    /// the part. Per-vendor variant lives on <c>VendorPart.HtsCode</c>;
    /// this is the part-level fallback used when no vendor-specific code is
    /// configured.
    /// </summary>
    public string? HtsCode { get; set; }

    /// <summary>
    /// UN/DOT hazmat class (e.g., "Class 3 Flammable"). Free-text for now —
    /// could later become a <c>reference_data</c> lookup.
    /// </summary>
    public string? HazmatClass { get; set; }

    /// <summary>
    /// For lot-tracked items with expiration. Drives expiry warnings on
    /// receipts when populated. Null = no shelf-life tracking.
    /// </summary>
    public int? ShelfLifeDays { get; set; }

    /// <summary>
    /// Per-item override of the global backflush default (Auto / Manual /
    /// None). Null = follow global default.
    /// </summary>
    public BackflushPolicy? BackflushPolicy { get; set; }

    /// <summary>
    /// True when the part ships as a kit (a fixed assortment of components
    /// shipped together), as opposed to a phantom assembly that explodes
    /// at MRP and is never picked as a single unit.
    /// </summary>
    public bool IsKit { get; set; }

    /// <summary>
    /// Marks a parent template that drives the configurator wizard. When
    /// true, the part itself isn't transactional — derived configured
    /// children are.
    /// </summary>
    public bool IsConfigurable { get; set; }

    /// <summary>
    /// Default put-away bin for receiving. FK to <c>storage_locations</c>.
    /// Speeds receiving / put-away by pre-filling the suggested bin.
    /// </summary>
    public int? DefaultBinId { get; set; }
    public StorageLocation? DefaultBin { get; set; }

    /// <summary>
    /// For Subcontract combos — points at the pre-finishing in-house part
    /// (raw / blank / unfinished) that gets sent out for the subcontracted
    /// step. Self-FK on <c>parts</c>.
    /// </summary>
    public int? SourcePartId { get; set; }

    // Accounting integration
    public string? ExternalId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Provider { get; set; }

    // Preferred vendor & auto-PO. Per-vendor sourcing terms (lead time,
    // MOQ, pack size) live on the VendorPart row — IPartSourcingResolver
    // reads them from the preferred VendorPart for sourcing-decision contexts.
    public int? PreferredVendorId { get; set; }
    public int SafetyStockQty { get; set; }
    public bool ExcludeFromAutoPo { get; set; }

    // Inventory thresholds & replenishment (Part-level — these are the
    // shop's own stocking policy, not a vendor characteristic).
    public decimal? MinStockThreshold { get; set; }
    public decimal? ReorderPoint { get; set; }
    public decimal? ReorderQuantity { get; set; }
    public int? SafetyStockDays { get; set; }

    // MRP planning
    public LotSizingRule? LotSizingRule { get; set; }
    public decimal? FixedOrderQuantity { get; set; }
    public decimal? MinimumOrderQuantity { get; set; }
    public decimal? OrderMultiple { get; set; }
    public int? PlanningFenceDays { get; set; }
    public int? DemandFenceDays { get; set; }
    public bool IsMrpPlanned { get; set; }

    // Receiving inspection
    public bool RequiresReceivingInspection { get; set; }
    public int? ReceivingInspectionTemplateId { get; set; }
    public ReceivingInspectionFrequency InspectionFrequency { get; set; } = ReceivingInspectionFrequency.Every;
    public int? InspectionSkipAfterN { get; set; }

    // Custom fields (JSONB)
    public string? CustomFieldValues { get; set; }

    // Units of measure
    public int? StockUomId { get; set; }
    public int? PurchaseUomId { get; set; }
    public int? SalesUomId { get; set; }
    public UnitOfMeasure? StockUom { get; set; }
    public UnitOfMeasure? PurchaseUom { get; set; }
    public UnitOfMeasure? SalesUom { get; set; }

    // Tooling association
    public int? ToolingAssetId { get; set; }
    public Asset? ToolingAsset { get; set; }

    public Vendor? PreferredVendor { get; set; }

    // Workflow Pattern Phase 2 / D3 — Per-record cost override + pointer at the
    // active CostCalculation snapshot. Read priority for displayed cost:
    //   ManualCostOverride ?? CostCalculation.ResultAmount ?? null.
    // Until D5 ships, only ManualCostOverride is ever populated.
    public decimal? ManualCostOverride { get; set; }
    public int? CurrentCostCalculationId { get; set; }
    public CostCalculation? CurrentCostCalculation { get; set; }

    // IActiveAware — Phase 3 H2 active-check. Parts use a status enum rather
    // than a bool. "Active" / "Prototype" / "Draft" are usable on new
    // transactions; "Obsolete" is not (treated as deactivated).
    public bool IsActiveForNewTransactions => Status != PartStatus.Obsolete;
    public string GetDisplayName() => string.IsNullOrWhiteSpace(PartNumber)
        ? Name
        : $"{PartNumber} ({Name})";

    public ICollection<BOMEntry> BOMEntries { get; set; } = [];
    public ICollection<BOMEntry> UsedInBOM { get; set; } = [];
    public ICollection<Operation> Operations { get; set; } = [];
    public ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; } = [];

    // UoM purchase-options effort — the sizes/forms this part is bought in
    // (4×8 sheet, 1 kg bar, bag of 100). Vendors price these via VendorPartPriceTier.
    public ICollection<PartPurchaseOption> PurchaseOptions { get; set; } = [];
    public ICollection<PartAlternate> Alternates { get; set; } = [];
    public ICollection<SerialNumber> SerialNumbers { get; set; } = [];

    // Phase 3 H4 / WU-20 — BOM revision history. CurrentBomRevisionId points
    // at the active immutable snapshot of this part's BOM. Older revisions
    // hang off the part via the BomRevisions collection but stay frozen.
    public int? CurrentBomRevisionId { get; set; }
    public BomRevision? CurrentBomRevision { get; set; }
    public ICollection<BomRevision> BomRevisions { get; set; } = [];
}
