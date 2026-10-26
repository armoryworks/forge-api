using Forge.Core.Entities.Accounting;

namespace Forge.Api.Features.Accounting.StockLoads;

/// <summary>
/// §7A opening inventory as a costed stock load: resolves and dry-runs the rows, posts them (bins, lots, the
/// valuation store and the Dr inventory / Cr Opening Balance Equity journal) and voids a posted load while every
/// loaded quantity is still where it was put.
/// </summary>
public interface IStockLoadService
{
    /// <summary>
    /// Resolves the caller's rows to parts and locations and prices them. An unknown part, an unknown or ambiguous
    /// location, or a bad quantity or cost throws a <see cref="FluentValidation.ValidationException"/> naming each row.
    /// </summary>
    Task<List<InventoryStockLoadLine>> ResolveLinesAsync(IReadOnlyList<StockLoadLineInput> input, CancellationToken ct = default);

    /// <summary>The dry run: per-line and load-level errors and warnings and the value per inventory account. No writes.</summary>
    Task<StockLoadValidationResult> ValidateAsync(InventoryStockLoad load, CancellationToken ct = default);

    /// <summary>Re-validates and posts a draft load in one transaction.</summary>
    Task PostAsync(int stockLoadId, int userId, CancellationToken ct = default);

    /// <summary>Takes a posted load back out: bins, lots, the valuation store and (OffsetEquity) the journal.</summary>
    Task VoidAsync(int stockLoadId, int userId, CancellationToken ct = default);

    /// <summary>The next free <c>SL-</c> number; call inside the transaction that inserts the load.</summary>
    Task<string> NextLoadNumberAsync(CancellationToken ct = default);

    /// <summary>The book, the posted opening journal's date and the inventory debits it already carries.</summary>
    Task<StockLoadContextModel> GetContextAsync(int? bookId, CancellationToken ct = default);

    /// <summary>One load with its lines for the screen.</summary>
    Task<StockLoadModel> GetModelAsync(int stockLoadId, CancellationToken ct = default);
}
