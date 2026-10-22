using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Read seam for the Phase-3 indirect-method Cash-Flow statement. Reconciles net income to the change in
/// cash via the working-capital changes over the window; ties out to the actual cash-account movement by the
/// double-entry identity. Read-only. A missing end is today; a missing start is the start of the fiscal year
/// containing the end (from inception when no fiscal year covers it), the same default as the P&amp;L.
/// </summary>
public interface ICashFlowStatementService
{
    Task<CashFlowStatement> GetCashFlowStatementAsync(
        int bookId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default);
}
