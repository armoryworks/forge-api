namespace Forge.Core.Models;

public record RevenueReportItem(
    string Period,
    string? CustomerName,
    int InvoiceCount,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    decimal AmountPaid);
