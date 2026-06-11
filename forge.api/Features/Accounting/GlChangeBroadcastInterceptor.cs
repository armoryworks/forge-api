using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Api.Hubs;
using Forge.Core.Entities.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Pushes an <c>accountingChanged</c> SignalR notification whenever GL / accounting data is written, so the
/// dark accounting screens auto-refresh no matter what triggered the change (receipts, material issues, COGS,
/// variances, period close, bank reconciliation, year-end close, …). Fires once per logical operation: after the
/// transaction commits when one is active — the posting flows wrap operational + GL writes in a transaction, so
/// clients never re-fetch pre-commit data — otherwise right after the auto-committed SaveChanges.
///
/// Registered as a singleton (EF caches its internal service provider per options identity, so a per-scope
/// interceptor instance would defeat that). Per-<see cref="DbContext"/> "dirty" state therefore lives in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed by the context instance (auto-evicted on GC), never in
/// an instance field. The broadcast is best-effort and fully swallowed — it can never add latency to, or fail,
/// the save/commit path.
/// </summary>
public sealed class GlChangeBroadcastInterceptor(IHubContext<AccountingHub> hub)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, StrongBox<bool>> _dirtyByContext = new();

    // ── Detect: flag the context when an accounting entity is in the change set ──
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        MarkIfAccountingChange(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        MarkIfAccountingChange(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // ── No ambient transaction → the save IS the commit; broadcast now ──
    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        BroadcastIfCommitted(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        BroadcastIfCommitted(eventData.Context);
        return ValueTask.FromResult(result);
    }

    // ── Transactional flows broadcast on commit, not before ──
    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => BroadcastIfDirty(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        BroadcastIfDirty(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => ClearDirty(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ClearDirty(eventData.Context);
        return Task.CompletedTask;
    }

    private void MarkIfAccountingChange(DbContext? context)
    {
        if (context is null) return;
        var box = _dirtyByContext.GetOrCreateValue(context);
        if (box.Value) return;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && IsAccountingEntity(entry.Entity))
            {
                box.Value = true;
                return;
            }
        }
    }

    private void BroadcastIfCommitted(DbContext? context)
    {
        // Defer to TransactionCommitted when a transaction is still open — broadcasting now would let clients
        // re-fetch pre-commit (and therefore stale, or rolled-back) data.
        if (context is null || context.Database.CurrentTransaction is not null) return;
        if (TakeDirty(context)) Broadcast();
    }

    private void BroadcastIfDirty(DbContext? context)
    {
        if (TakeDirty(context)) Broadcast();
    }

    private void ClearDirty(DbContext? context)
    {
        if (context is not null && _dirtyByContext.TryGetValue(context, out var box))
            box.Value = false;
    }

    private bool TakeDirty(DbContext? context)
    {
        if (context is not null && _dirtyByContext.TryGetValue(context, out var box) && box.Value)
        {
            box.Value = false;
            return true;
        }
        return false;
    }

    private void Broadcast()
    {
        // Fire-and-forget with no request CT: a notification must never add latency to — or fail — the save path.
        _ = SafeSendAsync();
    }

    private async Task SafeSendAsync()
    {
        try
        {
            await hub.Clients.All.SendAsync("accountingChanged", new { changedAt = DateTimeOffset.UtcNow });
        }
        catch
        {
            // Best-effort notification — swallow any transport/serialization error.
        }
    }

    private static bool IsAccountingEntity(object entity) => entity
        is JournalEntry or JournalLine or FiscalYear or FiscalPeriod or GlAccount
        or AccountDeterminationRule or BankReconciliation or BankReconciliationItem
        or InventoryValuation or LedgerBalance
        // Not a GL entity, but this hub IS the finance-ops push channel: transmission status changes
        // (Retrying/Failed/Succeeded) must auto-refresh the Payables lists without a manual reload.
        or Forge.Core.Entities.PaymentTransmission;
}
