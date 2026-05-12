using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ReceivingRecord : BaseAuditableEntity
{
    public int PurchaseOrderLineId { get; set; }
    // Phase 3 / WU-23 (F8-broad): receiving against a fractional-quantity PO
    // line preserves precision (PO line itself was promoted in WU-10).
    public decimal QuantityReceived { get; set; }
    public string? ReceivedBy { get; set; }
    public int? StorageLocationId { get; set; }
    public string? Notes { get; set; }
    public ReceivingInspectionStatus InspectionStatus { get; set; } = ReceivingInspectionStatus.NotRequired;
    public int? InspectedById { get; set; }
    public DateTimeOffset? InspectedAt { get; set; }
    public string? InspectionNotes { get; set; }
    public decimal? InspectedQuantityAccepted { get; set; }
    public decimal? InspectedQuantityRejected { get; set; }
    public int? QcInspectionId { get; set; }

    // ─── Bought-parts effort PR3 — landed cost capture ─────────────────
    /// <summary>
    /// Receipt grouping identifier. All <see cref="ReceivingRecord"/>s
    /// created in a single <c>ReceiveItems</c> call share this number
    /// (format: <c>R-YYYYMMDD-NNNN</c>). Acts as a de-facto shipment
    /// header without introducing a separate entity — the per-shipment
    /// freight invoice and allocation method live on the lines themselves.
    /// Null on records created before PR3 (legacy migration backfills as
    /// <c>R-LEGACY-{Id}</c> so each becomes its own one-line shipment).
    /// </summary>
    public string? ReceiptNumber { get; set; }

    /// <summary>
    /// Total freight on the receipt this record belongs to. Duplicated to
    /// every record sharing a <see cref="ReceiptNumber"/>; the per-line
    /// share is in <see cref="AllocatedFreight"/>. Distinct from the PO's
    /// <c>EstimatedFreight</c>: this is the *actual* invoice amount.
    /// Null when freight has not been captured yet (manual / pending).
    /// </summary>
    public decimal? ActualFreight { get; set; }

    /// <summary>
    /// How <see cref="ActualFreight"/> was split across the records of
    /// this receipt. Default <see cref="FreightAllocationMethod.ByExtendedValue"/>
    /// (proportional to qty × unit price). The chosen method is recorded
    /// per record so audit can replay the calc.
    /// </summary>
    public FreightAllocationMethod FreightAllocationMethod { get; set; } = FreightAllocationMethod.ByExtendedValue;

    /// <summary>
    /// This record's share of <see cref="ActualFreight"/>, computed at
    /// receive time using <see cref="FreightAllocationMethod"/>. Feeds the
    /// landed-cost calc directly. Null when freight has not been captured.
    /// </summary>
    public decimal? AllocatedFreight { get; set; }

    public PurchaseOrderLine PurchaseOrderLine { get; set; } = null!;
    public StorageLocation? StorageLocation { get; set; }
}
