using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ §7A conversion — posts the opening-balance journal at go-live (Source = Conversion): balance-sheet
/// opening balances + AR/AP open items (control accounts carry a party). Idempotent per book; the engine
/// enforces that the opening journal balances and that control lines carry a party.
/// </summary>
public interface IConversionService
{
    Task<OpeningBalanceResult> PostOpeningBalancesAsync(
        PostOpeningBalancesModel model, int postedByUserId, CancellationToken ct = default);
}
