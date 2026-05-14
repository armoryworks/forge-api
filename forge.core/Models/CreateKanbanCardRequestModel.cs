
namespace Forge.Core.Models;

public record CreateKanbanCardRequestModel
{
    public int PartId { get; init; }
    public int WorkCenterId { get; init; }
    public int? StorageLocationId { get; init; }
    public decimal BinQuantity { get; init; }
    public int NumberOfBins { get; init; } = 2;
    public KanbanSupplySource SupplySource { get; init; }
    public int? SupplyVendorId { get; init; }
    public int? SupplyWorkCenterId { get; init; }
    public decimal? LeadTimeDays { get; init; }
}
