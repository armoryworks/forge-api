namespace Forge.Core.Models;

public record KanbanCardResponseModel
{
    public int Id { get; init; }
    public string CardNumber { get; init; } = string.Empty;
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public int WorkCenterId { get; init; }
    public string WorkCenterName { get; init; } = string.Empty;
    public int? StorageLocationId { get; init; }
    public string? StorageLocationName { get; init; }
    public decimal BinQuantity { get; init; }
    public int NumberOfBins { get; init; }
    public string Status { get; init; } = string.Empty;
    public string SupplySource { get; init; } = string.Empty;
    public string? SupplyVendorName { get; init; }
    public decimal? LeadTimeDays { get; init; }
    public DateTimeOffset? LastTriggeredAt { get; init; }
    public DateTimeOffset? LastReplenishedAt { get; init; }
    public int? ActiveOrderId { get; init; }
    public string? ActiveOrderType { get; init; }
    public int TriggerCount { get; init; }
    public bool IsActive { get; init; }
}
