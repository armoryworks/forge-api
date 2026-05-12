namespace Forge.Core.Models;

public record OeeTrendPointModel
{
    public DateOnly Date { get; init; }
    public decimal Availability { get; init; }
    public decimal Performance { get; init; }
    public decimal Quality { get; init; }
    public decimal Oee { get; init; }
}
