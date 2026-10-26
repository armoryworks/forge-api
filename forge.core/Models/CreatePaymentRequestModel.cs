namespace Forge.Core.Models;

public record CreatePaymentRequestModel(
    int CustomerId,
    string Method,
    decimal Amount,
    DateTimeOffset PaymentDate,
    string? ReferenceNumber,
    string? Notes,
    List<CreatePaymentApplicationModel>? Applications,
    // Optional caller-supplied payment number. Honored only when the
    // payments.allow_manual_numbers setting is on; otherwise auto-generated.
    string? PaymentNumber = null,
    int? CurrencyId = null,
    decimal? FxRate = null);

public record CreatePaymentApplicationModel(
    int InvoiceId,
    decimal Amount,
    // Settlement FX rate (txn→functional) for this application (Phase-4 FULLGL). Null takes the payment's rate;
    // a foreign-currency settlement realizes FX vs the invoice's booking rate.
    decimal? SettlementFxRate = null,
    decimal? DiscountAmount = null,
    bool AllowDiscountOverride = false);
