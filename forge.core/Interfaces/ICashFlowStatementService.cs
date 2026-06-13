using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Read seam for the Phase-3 indirect-method Cash-Flow statement. Reconciles net income to the change in
/// cash via the working-capital changes over the window; ties out to the actual cash-account movement by the
/// double-entry identity. Read-only.
/// </summary>
public interface ICashFlowStatementService
{
    Task<CashFlowStatement> GetCashFlowStatementAsync(
        int bookId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default);
}
