namespace Forge.Core.Models;

public record CreatePaymentRequestModel(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications);

public record CreatePaymentApplicationModel(
    int InvoiceId,
    decimal Amount,
    // Settlement FX rate (txn→functional) for this application (Phase-4 FULLGL). Default 1 keeps single-
    // currency settlements unchanged; a foreign-currency settlement realizes FX vs the invoice's booking rate.
    decimal SettlementFxRate = 1m);
