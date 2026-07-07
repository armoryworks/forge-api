using System.ComponentModel.DataAnnotations;

using Forge.Core.Enums;

namespace Forge.Core.Entities;

/// <summary>
/// One stage of a staged production/shipment/payment plan on a sales order.
/// The derived backward-scheduling timeline stays advisory; stages are the
/// user-owned editable layer. A stage may link a shipment and a payment
/// milestone (shipping the stage flips the milestone Due), and lots attach
/// via LotRecord.SalesOrderStageId.
/// </summary>
public class SalesOrderStage : BaseAuditableEntity
{
    public int SalesOrderId { get; set; }
    public int Sequence { get; set; }
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    public SalesOrderStageStatus Status { get; set; } = SalesOrderStageStatus.Planned;
    public DateTimeOffset? PlannedProductionComplete { get; set; }
    public DateTimeOffset? PlannedShipDate { get; set; }
    public DateTimeOffset? ActualShipDate { get; set; }
    public int? ShipmentId { get; set; }
    public int? PaymentMilestoneId { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Shipment? Shipment { get; set; }
    public PaymentMilestone? PaymentMilestone { get; set; }
    public ICollection<SalesOrderStageLine> Lines { get; set; } = [];
    public ICollection<LotRecord> Lots { get; set; } = [];
}
