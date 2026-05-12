namespace Forge.Core.Models;

public record MyExpenseHistoryReportItem(
    int Id,
    string Category,
    string Description,
    decimal Amount,
    string Status,
    DateTimeOffset ExpenseDate,
    string? Vendor);
