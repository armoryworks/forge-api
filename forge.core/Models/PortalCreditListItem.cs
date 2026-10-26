namespace Forge.Core.Models;

/// <summary>A credit memo or a refund shown on the customer portal. <c>Kind</c> is <c>CreditMemo</c> or <c>Refund</c>; a refund has no open amount.</summary>
public record PortalCreditListItem(
    string Kind,
    int Id,
    string Number,
    DateTimeOffset Date,
    string Description,
    decimal Amount,
    decimal OpenAmount,
    string Status,
    string? CurrencyCode);
