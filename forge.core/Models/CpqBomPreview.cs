namespace Forge.Core.Models;

public record CpqBomPreview
{
    public int? PartId { get; init; }
    public string PartNumber { get; init; } = "";
    public decimal Quantity { get; init; }
    public string Source { get; init; } = "";
}
