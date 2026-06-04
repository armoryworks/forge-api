namespace Forge.Core.Models.Accounting;

/// <summary>
/// One account's debit/credit totals in a <see cref="TrialBalance"/> (§5.3).
/// Amounts are <b>functional</b> currency.
/// </summary>
public sealed class TrialBalanceRow
{
    public int GlAccountId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    public decimal DebitTotal { get; init; }
    public decimal CreditTotal { get; init; }

    /// <summary>Net = DebitTotal − CreditTotal (positive = net debit).</summary>
    public decimal NetBalance => DebitTotal - CreditTotal;
}

/// <summary>
/// A filter-immune trial balance for a book over a period / date range (§5.3).
/// Sums <b>functional</b> amounts of <c>Posted</c> entries only (Draft excluded;
/// Reversed nets out because both the original and its reversal are Posted and
/// equal-and-opposite). <see cref="IsBalanced"/> asserts total Dr == total Cr.
/// </summary>
public sealed class TrialBalance
{
    public int BookId { get; init; }

    /// <summary>Inclusive start of the date range (null = from inception).</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive end of the date range (null = open-ended).</summary>
    public DateOnly? ToDate { get; init; }

    public IReadOnlyList<TrialBalanceRow> Rows { get; init; } = [];

    public decimal TotalDebit { get; init; }
    public decimal TotalCredit { get; init; }

    /// <summary>True when total debits equal total credits (the ledger balances).</summary>
    public bool IsBalanced => TotalDebit == TotalCredit;
}
