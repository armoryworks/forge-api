using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-3 — bank reconciliation of a cash GL account against a bank statement. Clearing state is tracked
/// outside the immutable ledger (in reconciliation items). Read/modify seam: start a draft, toggle a line's
/// cleared flag, fetch the worksheet, finalize once reconciled.
/// </summary>
public interface IBankReconciliationService
{
    Task<BankReconciliationWorksheet> StartAsync(
        int bookId, int cashGlAccountId, DateOnly statementDate, decimal statementEndingBalance, CancellationToken ct = default);

    Task<BankReconciliationWorksheet> GetWorksheetAsync(int reconciliationId, CancellationToken ct = default);

    Task<BankReconciliationWorksheet> SetClearedAsync(
        int reconciliationId, long journalLineId, bool isCleared, CancellationToken ct = default);

    Task<BankReconciliationWorksheet> FinalizeAsync(int reconciliationId, CancellationToken ct = default);
}
