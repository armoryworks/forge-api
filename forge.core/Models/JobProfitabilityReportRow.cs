namespace Forge.Core.Models;

public record JobProfitabilityReportRow
{
    public int JobId { get; init; }
    public string JobNumber { get; init; } = string.Empty;
    public string JobTitle { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public decimal QuotedPrice { get; init; }
    public decimal ActualCost { get; init; }
    public decimal Margin { get; init; }
    public decimal MarginPercent { get; init; }
    public decimal MaterialCost { get; init; }
    public decimal LaborCost { get; init; }
    public decimal BurdenCost { get; init; }
    public decimal SubcontractCost { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
