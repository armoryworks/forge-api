using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// regulated-parts-safety C-2 / CAP-QC-RECALL — an immutable lot-based recall snapshot.
///
/// Initiated on a lot (typically a raw/supplier lot). The initiate handler walks the
/// <c>lot_consumptions</c> genealogy FORWARD to every produced lot that contains the
/// recalled material, resolves the shipments/customers that received them (at
/// sales-order-line granularity), quarantines matching on-hand bin contents, and freezes
/// the result as this snapshot. Mirrors the <see cref="BomRevision"/> parent+children
/// immutable pattern — only <see cref="Status"/>/<see cref="ResolvedAt"/> change afterward.
/// </summary>
public class Recall : BaseAuditableEntity
{
    public int InitiatedByUserId { get; set; }

    /// <summary>The lot the recall was initiated on (the root of the forward trace).</summary>
    public int InitiatedLotId { get; set; }
    public LotRecord InitiatedLot { get; set; } = null!;

    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset RecallDate { get; set; }
    public RecallStatus Status { get; set; } = RecallStatus.Active;

    // Denormalized snapshot rollups (frozen at initiation for fast reporting).
    public int AffectedLotsCount { get; set; }
    public int AffectedShipmentsCount { get; set; }
    public decimal TotalQuarantinedQuantity { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }

    /// <summary>Immutable set of affected lots (the recalled lot + every downstream produced lot).</summary>
    public ICollection<RecallAffectedLot> AffectedLots { get; set; } = [];

    /// <summary>Immutable set of shipments (and their customers) that carried affected lots.</summary>
    public ICollection<RecallAffectedShipment> AffectedShipments { get; set; } = [];
}
