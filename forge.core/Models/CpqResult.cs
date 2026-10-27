namespace Forge.Core.Models;

public record CpqResult
{
    public decimal ComputedPrice { get; init; }
    public decimal Quantity { get; init; } = 1m;
    public decimal ExtendedPrice { get; init; }
    public int LeadTimeDays { get; init; }
    public IReadOnlyList<CpqPriceBreakdown> PriceBreakdown { get; init; } = [];
    public IReadOnlyList<CpqBomPreview> BomPreview { get; init; } = [];
    public IReadOnlyList<CpqRoutingPreview> RoutingPreview { get; init; } = [];
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
    public bool IsValid { get; init; }
}
