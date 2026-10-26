namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>The stock-load dry run: every finding, the value per inventory account and the grand total.</summary>
public sealed record StockLoadValidationResult(
    bool IsValid,
    IReadOnlyList<StockLoadIssue> Issues,
    IReadOnlyList<StockLoadAccountTotal> AccountTotals,
    decimal GrandTotal);
