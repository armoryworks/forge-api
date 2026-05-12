namespace Forge.Core.Models;

public record CopqCategoryDetailResponseModel
{
    public string Category { get; init; } = string.Empty;
    public string SubCategory { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int EventCount { get; init; }
    public decimal PercentOfTotal { get; init; }
}
