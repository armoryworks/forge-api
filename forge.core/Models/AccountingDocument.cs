using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>
/// A sales document pushed to the connected accounting provider. <c>CurrencyCode</c> and <c>ExchangeRate</c>
/// are set only for a document in a currency other than the install's base currency. <c>Terms</c> is set when the
/// document carries payment terms.
/// </summary>
public record AccountingDocument(
    AccountingDocumentType Type,
    string CustomerExternalId,
    List<AccountingLineItem> LineItems,
    string? RefNumber,
    decimal Amount,
    DateTimeOffset Date,
    string? CurrencyCode = null,
    decimal? ExchangeRate = null,
    AccountingTerms? Terms = null);
