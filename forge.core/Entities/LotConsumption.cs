namespace Forge.Core.Entities;

/// <summary>
/// regulated-parts-safety C-2: a component-genealogy edge — an input lot consumed to produce
/// an output lot, written at backflush/issue. Powers CAP-QC-RECALL forward/backward trace and
/// quarantine; complements LotRecord's origin links (job/run/PO-line).
/// </summary>
public class LotConsumption : BaseAuditableEntity
{
    public int ConsumedLotId { get; set; }
    public LotRecord ConsumedLot { get; set; } = null!;

    public int ProducedLotId { get; set; }
    public LotRecord ProducedLot { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>Optional production context (no FK — advisory).</summary>
    public int? JobId { get; set; }
    public int? ProductionRunId { get; set; }
}
