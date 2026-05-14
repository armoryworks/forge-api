
namespace Forge.Core.Models;

public record UpdateKanbanCardRequestModel
{
    public decimal? BinQuantity { get; init; }
    public int? NumberOfBins { get; init; }
    public KanbanSupplySource? SupplySource { get; init; }
    public int? SupplyVendorId { get; init; }
    public int? SupplyWorkCenterId { get; init; }
    public int? StorageLocationId { get; init; }
    public decimal? LeadTimeDays { get; init; }
    public bool? IsActive { get; init; }
}
