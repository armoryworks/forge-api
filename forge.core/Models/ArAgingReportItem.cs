namespace Forge.Core.Models;

public record ArAgingReportItem(
    int InvoiceId,
    string InvoiceNumber,
    string CustomerName,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    int DaysOverdue,
    string AgingBucket);
