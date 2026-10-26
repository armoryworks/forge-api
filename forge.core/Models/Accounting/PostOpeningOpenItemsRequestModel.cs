namespace Forge.Core.Models.Accounting;

/// <summary>
/// Body of <c>POST accounting/conversion/open-items</c>: the open items to post as the book's opening-balance
/// entry and the legacy AR and AP control balances their totals must tie to.
/// </summary>
public record PostOpeningOpenItemsRequestModel(
    int BookId,
    DateOnly AsOfDate,
    decimal ReceivablesControl,
    decimal PayablesControl,
    List<OpeningOpenItemRowModel> Rows);
