namespace Forge.Core.Models;

public record CreditMemoApplicationModel(
    int Id,
    int CreditMemoId,
    string? CreditMemoNumber,
    int InvoiceId,
    string? InvoiceNumber,
    decimal Amount,
    DateTimeOffset AppliedAt,
    DateTimeOffset? ReversedAt);
