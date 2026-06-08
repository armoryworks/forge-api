using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ProductionRun : BaseAuditableEntity
{
    public int JobId { get; set; }
    public int PartId { get; set; }
    public int? OperatorId { get; set; }
    public int? WorkCenterId { get; set; }
    public string RunNumber { get; set; } = string.Empty;
    public int TargetQuantity { get; set; }
    public int CompletedQuantity { get; set; }
    public int ScrapQuantity { get; set; }
    public int ReworkQuantity { get; set; }
    public ProductionRunStatus Status { get; set; } = ProductionRunStatus.Planned;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    // Finished-goods receipt: set once the run's good output is received into stock (the receive-to-stock
    // step). ReceivedQuantity captures the good quantity stocked at that moment (immutable record, independent
    // of any later CompletedQuantity edit). Null ReceivedToStockAt ⇒ not yet received (the idempotency guard).
    public DateTimeOffset? ReceivedToStockAt { get; set; }
    public int ReceivedQuantity { get; set; }
    public string? Notes { get; set; }
    public decimal? SetupTimeMinutes { get; set; }
    public decimal? RunTimeMinutes { get; set; }
    public decimal? IdealCycleTimeSeconds { get; set; }
    public decimal? ActualCycleTimeSeconds { get; set; }

    public Job Job { get; set; } = null!;
    public Part Part { get; set; } = null!;
    public WorkCenter? WorkCenter { get; set; }

    /// <summary>
    /// First-pass yield % = good units / total units processed = Completed / (Completed + Scrap).
    /// <see cref="CompletedQuantity"/> is the GOOD count and <see cref="ScrapQuantity"/> the bad count —
    /// they are disjoint (the UpdateProductionRun validator enforces Completed + Scrap ≤ Target). The old
    /// <c>(Completed − Scrap)/Completed</c> form was wrong on both numerator and denominator. Returns 0 when
    /// nothing has been processed.
    /// </summary>
    public static decimal YieldPercent(int completedQuantity, int scrapQuantity)
    {
        var total = completedQuantity + scrapQuantity;
        return total > 0 ? completedQuantity * 100.0m / total : 0m;
    }
}
