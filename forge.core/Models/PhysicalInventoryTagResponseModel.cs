using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PhysicalInventoryTagResponseModel
{
    public int Id { get; init; }
    public int PhysicalInventoryId { get; init; }
    public int TagNumber { get; init; }
    public int LocationId { get; init; }
    public string LocationName { get; init; } = string.Empty;
    public string? LocationBarcode { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string? PartDescription { get; init; }
    public string? LotNumber { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? FrozenQuantity { get; init; }
    public decimal? CountedQuantity { get; init; }
    public decimal? RecountQuantity { get; init; }
    public decimal? FinalQuantity { get; init; }
    public PhysicalInventoryTagStatus Status { get; init; }
    public DateTimeOffset? CountedAt { get; init; }
    public DateTimeOffset? RecountedAt { get; init; }
    public string? Notes { get; init; }
}
