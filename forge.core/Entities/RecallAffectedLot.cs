namespace Forge.Core.Entities;

/// <summary>
/// One lot caught in a recall's forward genealogy trace (the recalled lot itself, or a
/// downstream produced lot that consumed it). Records the on-hand quantity found and the
/// portion quarantined (moved to <c>BinContentStatus.QcHold</c>) at initiation time.
/// </summary>
public class RecallAffectedLot : BaseAuditableEntity
{
    public int RecallId { get; set; }
    public Recall Recall { get; set; } = null!;

    public int LotId { get; set; }
    public LotRecord Lot { get; set; } = null!;

    /// <summary>Quantity of the recalled material that flowed into this lot (0 for the root lot).</summary>
    public decimal ConsumedQuantity { get; set; }

    public int? JobId { get; set; }
    public int? ProductionRunId { get; set; }

    /// <summary>On-hand quantity of this lot found across bins at initiation.</summary>
    public decimal OnHandQuantity { get; set; }

    /// <summary>Portion of on-hand moved to QC hold (quarantined) by this recall.</summary>
    public decimal QuarantinedQuantity { get; set; }
}
