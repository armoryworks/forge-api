using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-4b — period-end unrealized FX revaluation of the net foreign monetary position. CAP-ACCT-FXREVAL
/// gated. Realized FX (on settlement) is a separate hook in the payment/settlement services.
/// </summary>
public interface IFxRevaluationService
{
    Task<FxRevaluationResult> RevalueAsync(
        int bookId, int currencyId, decimal newRate, DateOnly asOf, int postedByUserId, CancellationToken ct = default);
}
