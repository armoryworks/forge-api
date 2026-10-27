namespace Forge.Core.Models;

public record AddPhysicalInventoryTagRequestModel
{
    public int LocationId { get; init; }
    public int PartId { get; init; }
    public string? LotNumber { get; init; }
    public string? SerialNumber { get; init; }
    public decimal? Quantity { get; init; }
    public string? Notes { get; init; }
}
