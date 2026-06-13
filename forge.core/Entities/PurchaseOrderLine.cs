namespace Forge.Core.Entities;

public class PurchaseOrderLine : BaseEntity
{
    public int PurchaseOrderId { get; set; }
    public int PartId { get; set; }
    public string Description { get; set; } = string.Empty;
    // Phase 3 / WU-10 / F8-partial: quantities are decimal, not int. UoM-aware
    // shops need fractional quantities — material-by-weight (lb, kg), by-time
    // (hr), by-volume (gal, l). decimal(18, 4) = 4 fractional places, plenty
    // for any reasonable UoM. Cases EDGE-DECIMAL-PRECISION-001 / -004.
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    // Phase 3 / WU-14 / H3 — short-close support. When the PO is short-closed
    // (vendor backorder cancelled, item discontinued), the unreceived quantity
    // is captured here so the line stays around for historical accuracy but
    // the system understands it will not be received. Always 0 for normal POs;
    // set on /short-close to (OrderedQuantity - ReceivedQuantity at close-time).
    public decimal CancelledShortCloseQuantity { get; set; }

    // Phase-2 STAGE-D 3-way match — quantity of this line already billed by approved VendorBills, so the
    // GRNI accrued at receipt is cleared only once. UnbilledReceivedQuantity = received − billed is the
    // open GRNI a new bill may clear. Always 0 for lines never billed through the AP sub-ledger.
    public decimal BilledQuantity { get; set; }

    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; }
    public int? MrpPlannedOrderId { get; set; }
    public int? UomId { get; set; }

    // UoM purchase-units effort — which PartPurchaseUnit (size/form) was ordered. When set,
    // OrderedQuantity counts in options and UnitPrice is per option; the base-UoM quantity (for
    // receiving into the bin + landed cost) = qty × option.ContentQuantity. Null = ordered per
    // base unit (legacy single-option behavior).
    public int? PurchaseUnitId { get; set; }

    // Optional reason captured when a user manually overrides the suggested
    // (vendor-tier) unit price on this line. Null when not overridden.
    public string? ManualOverrideReason { get; set; }

    // Phase 3 / WU-14 — RemainingQuantity excludes cancelled-short-close so
    // a short-closed line reports 0 remaining, not the unreceived portion.
    public decimal RemainingQuantity => OrderedQuantity - ReceivedQuantity - CancelledShortCloseQuantity;
    public decimal UnreceivedQuantity => OrderedQuantity - ReceivedQuantity;

    /// <summary>Received but not yet billed — the open GRNI a 3-way-match bill may clear (STAGE D).</summary>
    public decimal UnbilledReceivedQuantity => ReceivedQuantity - BilledQuantity;

    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public MrpPlannedOrder? MrpPlannedOrder { get; set; }
    public UnitOfMeasure? Uom { get; set; }
    public PartPurchaseUnit? PurchaseUnit { get; set; }
    public ICollection<ReceivingRecord> ReceivingRecords { get; set; } = [];
    public ICollection<PurchaseOrderRelease> Releases { get; set; } = [];
}
