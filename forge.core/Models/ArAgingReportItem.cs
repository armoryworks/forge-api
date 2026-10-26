namespace Forge.Core.Models;

/// <summary>
/// One open AR document on the operational aging report. <c>DocumentType</c> is <c>Invoice</c>, or <c>CreditMemo</c>
/// for an open credit (then <c>InvoiceId</c>/<c>InvoiceNumber</c> carry the memo's id and number and the amounts are
/// negative, so a customer's total nets).
/// </summary>
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
    string AgingBucket,
    DateTimeOffset? DiscountDeadline = null,
    decimal DiscountAvailable = 0m,
    string DocumentType = "Invoice");
