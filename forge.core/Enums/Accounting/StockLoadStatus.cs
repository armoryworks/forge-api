namespace Forge.Core.Enums.Accounting;

/// <summary>Lifecycle of an opening stock load. A <c>Draft</c> is editable; <c>Posted</c> has written the bins,
/// the valuation store and (in OffsetEquity mode) the journal; <c>Voided</c> has taken all of that back out.</summary>
public enum StockLoadStatus
{
    Draft = 0,
    Posted = 1,
    Voided = 2,
}
