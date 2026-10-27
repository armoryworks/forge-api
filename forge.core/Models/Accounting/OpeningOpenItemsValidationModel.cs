namespace Forge.Core.Models.Accounting;

/// <summary>
/// Dry run of the opening AR/AP open items: per-row results, the receivable and payable totals the tie-out
/// compares with the legacy control balances, and whether the book's opening-balance entry is already posted.
/// </summary>
public record OpeningOpenItemsValidationModel(
    bool AlreadyPosted,
    decimal ReceivablesTotal,
    decimal PayablesTotal,
    int ErrorCount,
    IReadOnlyList<OpeningOpenItemRowResultModel> Rows);
