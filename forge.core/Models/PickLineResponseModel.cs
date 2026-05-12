using Forge.Core.Enums;

namespace Forge.Core.Models;

public record PickLineResponseModel
{
    public int Id { get; init; }
    public int ShipmentLineId { get; init; }
    public int PartId { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string PartDescription { get; init; } = string.Empty;
    public string FromLocationName { get; init; } = string.Empty;
    public string? BinPath { get; init; }
    public decimal RequestedQuantity { get; init; }
    public decimal PickedQuantity { get; init; }
    public PickLineStatus Status { get; init; }
    public int SortOrder { get; init; }
    public DateTimeOffset? PickedAt { get; init; }
    public string? ShortNotes { get; init; }
}
