namespace Forge.Core.Models;

public record ExpenseSummaryReportItem(
    string Category,
    decimal TotalAmount,
    int Count);
