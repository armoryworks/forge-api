namespace Forge.Core.Models;

public record CreateInvoiceRequestModel(
    int CustomerId,
    int? SalesOrderId,
    int? ShipmentId,
    DateTimeOffset InvoiceDate,
    DateTimeOffset DueDate,
    string? CreditTerms,
    decimal TaxRate,
    string? Notes,
    List<CreateInvoiceLineModel> Lines,
    // Multi-currency (Phase-4 FULLGL, additive). Null CurrencyId resolves to the active book's functional
    // currency; FxRate is the booking rate (txn→functional). Defaults (null / 1) keep single-currency callers
    // unchanged — the invoice books in functional currency at unity.
    int? CurrencyId = null,
    decimal FxRate = 1m,
    // Optional caller-supplied invoice number. Honored only when the
    // invoices.allow_manual_numbers setting is on; otherwise auto-generated.
    string? InvoiceNumber = null);

public record CreateInvoiceLineModel(
    int? PartId,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
