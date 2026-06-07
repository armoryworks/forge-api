using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — year-end close / Retained-Earnings roll-forward. Posts a single closing journal entry that
/// zeroes every Income/Expense account into the <c>RETAINED_EARNINGS</c> account (net income → Cr RE, net
/// loss → Dr RE), then hard-closes every period in the year and marks the year Closed. Idempotent via the
/// engine's <c>(BookId, IdempotencyKey)</c> de-dupe; an already-closed year is rejected.
/// </summary>
public interface IYearEndCloseService
{
    Task<YearEndCloseResult> CloseYearAsync(int fiscalYearId, int closedByUserId, CancellationToken ct = default);
}
