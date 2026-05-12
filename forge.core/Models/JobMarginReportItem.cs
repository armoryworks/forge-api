namespace Forge.Core.Models;

public record JobMarginReportItem(
    string JobNumber,
    string Title,
    string? CustomerName,
    decimal Revenue,
    decimal LaborCost,
    decimal MaterialCost,
    decimal ExpenseCost,
    decimal TotalCost,
    decimal Margin,
    decimal MarginPercentage);
