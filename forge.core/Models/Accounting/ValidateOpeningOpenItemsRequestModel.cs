namespace Forge.Core.Models.Accounting;

/// <summary>Body of <c>POST accounting/opening-balances/validate</c>: the open items to check before posting.</summary>
public record ValidateOpeningOpenItemsRequestModel(int BookId, List<OpeningOpenItemRowModel> Rows);
