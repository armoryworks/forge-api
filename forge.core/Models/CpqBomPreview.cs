namespace Forge.Core.Models;

public record CpqBomPreview
{
    public string PartNumber { get; init; } = "";
    public decimal Quantity { get; init; }
    public string Source { get; init; } = "";
}
