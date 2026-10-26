using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// A draft credit memo. With <c>InvoiceId</c> the memo takes the invoice's currency, booking rate and tax rate;
/// <c>CurrencyId</c>, <c>FxRate</c> and <c>TaxRate</c> apply to a standalone memo.
/// </summary>
public record CreateCreditMemoRequestModel(
    int CustomerId,
    int? InvoiceId,
    CreditMemoReasonType ReasonType,
    string Reason,
    DateTimeOffset? CreditDate,
    List<CreditMemoLineRequestModel> Lines,
    decimal? TaxRate = null,
    int? CurrencyId = null,
    decimal? FxRate = null,
    string? Notes = null);
