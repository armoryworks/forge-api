namespace Forge.Core.Models.Accounting;

/// <summary>
/// One line of the journal a pay run will post, with the account its determination key resolves to (null
/// when the key is not mapped in the book, which would stop the post).
/// </summary>
public record PayRunJournalPreviewLineModel(
    string AccountKey,
    int? GlAccountId,
    string? AccountNumber,
    string? AccountName,
    decimal Debit,
    decimal Credit,
    string? Description);
