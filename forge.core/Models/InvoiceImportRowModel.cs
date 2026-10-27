namespace Forge.Core.Models;

/// <summary>
/// One CSV row of a historical invoice import. Rows sharing an invoice number are one invoice; the header
/// columns (customer, dates, tax, total, paid amount, status) are read from the first of them and must not
/// disagree on later rows. Each row with a description, part or unit price is a line; an invoice with no
/// line columns gets one line for its total less tax.
/// </summary>
public record InvoiceImportRowModel(
    string? RowKey,
    string? InvoiceNumber,
    string? Customer,
    DateTimeOffset? InvoiceDate,
    DateTimeOffset? DueDate,
    string? Part,
    string? Description,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? Tax,
    decimal? Total,
    decimal? PaidAmount,
    DateTimeOffset? PaidDate,
    string? Status,
    string? CustomerPO);
