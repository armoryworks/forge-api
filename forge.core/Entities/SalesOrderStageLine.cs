namespace Forge.Core.Entities;

/// <summary>
/// Quantity allocation of one SO line to one stage. The sum of stage-line
/// quantities per SO line must not exceed the line quantity (validated in the
/// upsert handler).
/// </summary>
public class SalesOrderStageLine : BaseAuditableEntity
{
    public int SalesOrderStageId { get; set; }
    public int SalesOrderLineId { get; set; }
    public decimal Quantity { get; set; }

    public SalesOrderStage SalesOrderStage { get; set; } = null!;
    public SalesOrderLine SalesOrderLine { get; set; } = null!;
}
