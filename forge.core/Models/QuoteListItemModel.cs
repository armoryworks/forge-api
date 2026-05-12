namespace Forge.Core.Models;

public record QuoteListItemModel(
    int Id,
    string QuoteNumber,
    int CustomerId,
    string CustomerName,
    string Status,
    int LineCount,
    decimal Total,
    DateTimeOffset? ExpirationDate,
    DateTimeOffset CreatedAt);
