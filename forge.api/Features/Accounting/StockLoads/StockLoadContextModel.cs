namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// What the opening-stock screen needs before any load exists: the book, the posted opening journal's date, and
/// the inventory debits that journal already carries (when there are any, the "valuation only" mode is offered).
/// </summary>
public sealed record StockLoadContextModel(
    int BookId,
    string BookName,
    DateOnly? OpeningJournalDate,
    IReadOnlyList<StockLoadAccountTotal> OpeningJournalInventory,
    bool ConnectedMode);
