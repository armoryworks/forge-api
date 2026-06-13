using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Phase-1 STAGE D read seam (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "AR
/// sub-ledger + aging"). Derives the AR sub-ledger + aging <b>from the ledger</b>
/// — posted <c>JournalLine</c>s on AR-control accounts
/// (<c>GlAccount.ControlType == AR</c>) carrying
/// <c>SubledgerPartyType = Customer</c> — so the sub-ledger is never a parallel
/// store that can drift from the GL; it IS the GL, projected per customer and
/// bucketed by age.
/// <para>
/// <b>Filter-immune</b>, like the trial balance (§5.3): it ignores the global
/// soft-delete query filter (<c>IgnoreQueryFilters</c>) so a soft-deleted party
/// master or ledger row can never silently drop and make a sub-ledger appear to
/// reconcile when it does not (§5.1).
/// </para>
/// </summary>
public interface IArAgingService
{
    /// <summary>
    /// Builds the AR sub-ledger aging for <paramref name="bookId"/> as of
    /// <paramref name="asOfDate"/> (null = today in the engine's clock). Returns
    /// one row per customer with a non-zero open balance, bucketed by the age of
    /// each posting's <c>EntryDate</c>, plus an AR-control-vs-aging
    /// reconciliation.
    /// </summary>
    Task<ArAging> GetArAgingAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default);

    /// <summary>
    /// Reconciles the derived aging total against the posted AR-control account
    /// balance for the book (§9 "sub-ledger↔control reconciliation"). A non-zero
    /// <see cref="ArAgingReconciliation.Difference"/> indicates an AR-control
    /// posting with no customer party (the engine should reject those on control
    /// lines) or an out-of-band mutation — a bug to alert on.
    /// </summary>
    Task<ArAgingReconciliation> ReconcileAsync(
        int bookId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default);
}
