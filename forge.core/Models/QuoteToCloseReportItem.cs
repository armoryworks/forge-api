namespace Forge.Core.Models;

public record QuoteToCloseReportItem(
    string Status,
    int Count,
    decimal TotalValue);
