using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class KanbanCard : BaseAuditableEntity
{
    public string CardNumber { get; set; } = string.Empty;
    public int PartId { get; set; }
    public int WorkCenterId { get; set; }
    public int? StorageLocationId { get; set; }
    public decimal BinQuantity { get; set; }
    public int NumberOfBins { get; set; } = 2;
    public KanbanCardStatus Status { get; set; } = KanbanCardStatus.Full;
    public KanbanSupplySource SupplySource { get; set; }
    public int? SupplyVendorId { get; set; }
    public int? SupplyWorkCenterId { get; set; }
    public decimal? LeadTimeDays { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset? LastReplenishedAt { get; set; }
    public int? ActiveOrderId { get; set; }
    public string? ActiveOrderType { get; set; }
    public int TriggerCount { get; set; }
    public bool IsActive { get; set; } = true;

    public Part Part { get; set; } = null!;
    public WorkCenter WorkCenter { get; set; } = null!;
    public StorageLocation? StorageLocation { get; set; }
    public Vendor? SupplyVendor { get; set; }
    public ICollection<KanbanTriggerLog> TriggerLogs { get; set; } = [];
}
