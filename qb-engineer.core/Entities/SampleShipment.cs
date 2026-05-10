namespace QBEngineer.Core.Entities;

/// <summary>
/// Phase 1r / Batch 16 — pre-quote sample part tracking. Common B2B-mfg
/// flow: prospect gets a sample part (free or at cost) before committing
/// to a quote/PO. The sample is a real shipment with a real cost and
/// should factor into CAC for that lead. Today's model has no shape for
/// this — sample shipments either get logged in the regular Shipments
/// table (misleading — there's no SO behind them) or in free-text notes
/// (unreportable).
///
/// Lifecycle:
///   Requested → Approved → Shipped → Delivered → Outcome (Quoted/Lost/Stale)
/// </summary>
public class SampleShipment : BaseAuditableEntity
{
    public int LeadId { get; set; }
    public int? PartId { get; set; }
    /// <summary>Free-text part description when no formal Part row exists yet (common pre-quote).</summary>
    public string? PartDescription { get; set; }
    public int Quantity { get; set; } = 1;
    public string Status { get; set; } = "Requested";
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    /// <summary>Cost-to-us — material + labor on the sample. Counts toward CAC.</summary>
    public decimal? CostToUs { get; set; }
    /// <summary>Did we charge the prospect? Free samples leave this null.</summary>
    public decimal? ChargedAmount { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string? Notes { get; set; }

    public Lead Lead { get; set; } = null!;
}
