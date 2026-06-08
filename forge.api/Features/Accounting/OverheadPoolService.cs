using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Services;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>Result of a period overhead-pool close — the actual pool, the applied total, and the spending variance.</summary>
public sealed record OverheadPoolCloseResult(decimal ActualOverhead, decimal AppliedOverhead, decimal SpendingVariance, bool Posted);

/// <summary>
/// Phase-4 standard costing — single-plant overhead pool + spending (budget) variance. Actual overhead (rent,
/// utilities, admin, depreciation, …) is recorded into the <c>OVERHEAD_CONTROL</c> pool as it is incurred
/// (Dr OVERHEAD_CONTROL / Cr ACCRUED_EXPENSE); jobs absorb overhead at standard via the job-cost close
/// (Cr OVERHEAD_APPLIED). At period end the pool is closed: actual pool vs applied = the <b>overhead spending
/// variance</b> (under-applied = unfavorable debit, over-applied = favorable credit), and both the control and
/// applied balances are cleared to zero.
///
/// <para>This is the period-level companion to the per-job overhead EFFICIENCY variance (recognized at the
/// job-cost close): spending answers "did total overhead cost more/less than we absorbed", efficiency answers
/// "did each job use more/fewer driver hours than standard". Dark behind CAP-ACCT-FULLGL.</para>
/// </summary>
public interface IOverheadPoolService
{
    /// <summary>Records actual overhead incurred into the pool: Dr OVERHEAD_CONTROL / Cr ACCRUED_EXPENSE.</summary>
    Task RecordActualOverheadAsync(decimal amount, string memo, DateOnly entryDate, int userId, CancellationToken ct = default);

    /// <summary>Closes the pool as of a date: posts the spending variance (actual − applied) and clears the
    /// OVERHEAD_CONTROL + OVERHEAD_APPLIED balances to zero. Idempotent per close date.</summary>
    Task<OverheadPoolCloseResult> CloseOverheadPoolAsync(DateOnly asOf, int userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class OverheadPoolService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    ISystemAuditWriter? auditWriter = null) : IOverheadPoolService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeyOverheadControl = "OVERHEAD_CONTROL";
    private const string KeyOverheadApplied = "OVERHEAD_APPLIED";
    private const string KeyOverheadSpendingVariance = "OVERHEAD_SPENDING_VARIANCE";
    private const string KeyAccruedExpense = "ACCRUED_EXPENSE";

    public async Task RecordActualOverheadAsync(
        decimal amount, string memo, DateOnly entryDate, int userId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability) || amount <= 0m)
            return;

        var book = await ActiveBookAsync(ct);

        await postingEngine.PostAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "OverheadPool",
            CurrencyId = book.FunctionalCurrencyId,
            Memo = string.IsNullOrWhiteSpace(memo) ? "Actual overhead incurred" : memo,
            // Each accrual is a distinct event (no natural key) — a unique key satisfies the engine's
            // non-Manual-source requirement without de-duping legitimate repeated accruals.
            IdempotencyKey = $"{JournalSource.Inventory}:Overhead:Record:{Guid.NewGuid():N}",
            Lines =
            [
                new PostingLine { AccountKey = KeyOverheadControl, Debit = amount, Description = $"Actual overhead — {memo}" },
                new PostingLine { AccountKey = KeyAccruedExpense, Credit = amount, Description = $"Accrued overhead — {memo}" },
            ],
        }, userId, ct);
    }

    public async Task<OverheadPoolCloseResult> CloseOverheadPoolAsync(
        DateOnly asOf, int userId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return new OverheadPoolCloseResult(0m, 0m, 0m, Posted: false);

        var book = await ActiveBookAsync(ct);

        var closeKey = $"{JournalSource.Inventory}:Overhead:Close:{asOf:yyyyMMdd}";
        var alreadyClosed = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == closeKey, ct);
        if (alreadyClosed)
            return new OverheadPoolCloseResult(0m, 0m, 0m, Posted: false);

        var controlId = await AccountIdAsync(book.Id, KeyOverheadControl, ct);
        var appliedId = await AccountIdAsync(book.Id, KeyOverheadApplied, ct);
        if (controlId is null || appliedId is null)
            return new OverheadPoolCloseResult(0m, 0m, 0m, Posted: false);

        // Actual pool = net debit of OVERHEAD_CONTROL; applied = net credit of OVERHEAD_APPLIED (both to-date,
        // ≤ asOf). The close clears both balances, so a subsequent period accumulates fresh.
        var actualPool = Math.Round(await AccountBalanceAsync(book.Id, controlId.Value, asOf, debitPositive: true, ct), 2);
        var applied = Math.Round(await AccountBalanceAsync(book.Id, appliedId.Value, asOf, debitPositive: false, ct), 2);
        if (actualPool == 0m && applied == 0m)
            return new OverheadPoolCloseResult(0m, 0m, 0m, Posted: false);

        var spending = Math.Round(actualPool - applied, 2); // under-applied (>0) = unfavorable

        var lines = new List<PostingLine>();
        // Clear the actual pool (debit balance) and the applied (credit balance).
        if (actualPool > 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadControl, Credit = actualPool, Description = $"Clear overhead pool — {asOf}" });
        else if (actualPool < 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadControl, Debit = -actualPool, Description = $"Clear overhead pool — {asOf}" });
        if (applied > 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadApplied, Debit = applied, Description = $"Clear overhead applied — {asOf}" });
        else if (applied < 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadApplied, Credit = -applied, Description = $"Clear overhead applied — {asOf}" });
        // Spending variance = actual − applied.
        if (spending > 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadSpendingVariance, Debit = spending, Description = $"Overhead spending variance (unfavorable / under-applied) — {asOf}" });
        else if (spending < 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadSpendingVariance, Credit = -spending, Description = $"Overhead spending variance (favorable / over-applied) — {asOf}" });

        var entry = await postingEngine.PostAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = asOf,
            Source = JournalSource.Inventory,
            SourceType = "OverheadPool",
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Overhead pool close — {asOf}",
            IdempotencyKey = closeKey,
            Lines = lines,
        }, userId, ct);

        await TryAuditAsync(asOf, entry, actualPool, applied, spending, userId, ct);

        return new OverheadPoolCloseResult(actualPool, applied, spending, Posted: true);
    }

    private async Task<Book> ActiveBookAsync(CancellationToken ct)
        => await db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
           ?? throw new PostingException("NO_POSTING_BOOK",
               "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded for the overhead pool.");

    private async Task<int?> AccountIdAsync(int bookId, string key, CancellationToken ct)
        => await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == bookId && r.Key == key)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);

    private async Task<decimal> AccountBalanceAsync(int bookId, int accountId, DateOnly asOf, bool debitPositive, CancellationToken ct)
        => await (from line in db.JournalLines.IgnoreQueryFilters()
                  join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
                  where je.BookId == bookId
                      && line.GlAccountId == accountId
                      && je.EntryDate <= asOf
                      && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
                  select debitPositive ? line.Debit - line.Credit : line.Credit - line.Debit)
            .SumAsync(ct);

    private async Task TryAuditAsync(
        DateOnly asOf, JournalEntry entry, decimal actualPool, decimal applied, decimal spending, int actorUserId, CancellationToken ct)
    {
        if (auditWriter is null)
            return;

        try
        {
            var details = JsonSerializer.Serialize(new
            {
                before = (object?)null,
                after = new
                {
                    journalEntryId = entry.Id,
                    entryNumber = entry.EntryNumber,
                    bookId = entry.BookId,
                    asOf = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    actualOverhead = actualPool.ToString(CultureInfo.InvariantCulture),
                    appliedOverhead = applied.ToString(CultureInfo.InvariantCulture),
                    spendingVariance = spending.ToString(CultureInfo.InvariantCulture),
                },
                reason = $"Overhead pool close {asOf} — spending variance recognized, pool cleared.",
            });

            await auditWriter.WriteAsync(
                action: "GlOverheadPoolClosed",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Overhead pool close audit write failed for {AsOf} (entry {EntryId}); posting itself is committed.",
                asOf, entry.Id);
        }
    }
}
