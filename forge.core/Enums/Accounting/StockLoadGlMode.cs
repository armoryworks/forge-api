namespace Forge.Core.Enums.Accounting;

/// <summary>How an opening stock load reaches the ledger.</summary>
public enum StockLoadGlMode
{
    /// <summary>Dr the inventory control account of each part's class, Cr Opening Balance Equity.</summary>
    OffsetEquity = 0,

    /// <summary>
    /// Bins and valuation store only, no journal: for a book whose posted opening journal already debited the
    /// inventory accounts. Capped per account at that opening debit.
    /// </summary>
    ValuationOnly = 1,
}
