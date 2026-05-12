using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ReceivingInspection : BaseAuditableEntity
{
    public int ReceivingRecordId { get; set; }
    public int? QcInspectionId { get; set; }
    public ReceivingInspectionResult Result { get; set; }
    public decimal? AcceptedQuantity { get; set; }
    public decimal? RejectedQuantity { get; set; }
    public string? Notes { get; set; }
    public int InspectedById { get; set; }
    public DateTimeOffset InspectedAt { get; set; }
    public int? NcrId { get; set; }

    public ReceivingRecord ReceivingRecord { get; set; } = null!;
    public QcInspection? QcInspection { get; set; }
}
