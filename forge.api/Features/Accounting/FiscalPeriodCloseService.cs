using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class FiscalPeriodCloseService(AppDbContext db, IClock clock, IPostingEngine postingEngine)
    : IFiscalPeriodCloseService
{
    // Legal transitions. HardClosed is terminal (a hard-closed period is reopened only by an explicit
    // back-out at the DB level — never through this API). Open can hard-close directly (skipping soft).
    private static readonly Dictionary<FiscalPeriodStatus, FiscalPeriodStatus[]> Allowed = new()
    {
        [FiscalPeriodStatus.Open] = [FiscalPeriodStatus.SoftClosed, FiscalPeriodStatus.HardClosed],
        [FiscalPeriodStatus.SoftClosed] = [FiscalPeriodStatus.Open, FiscalPeriodStatus.HardClosed],
        [FiscalPeriodStatus.HardClosed] = [],
    };

    public async Task<FiscalPeriodModel> TransitionAsync(
        int periodId, FiscalPeriodStatus target, int actorUserId, CancellationToken ct = default)
    {
        var period = await db.FiscalPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new KeyNotFoundException($"Fiscal period {periodId} not found.");

        // Lock the period row so a concurrent post (which locks the same row in the engine) serializes with
        // this close, and reload to act on the committed status. Postgres only; a no-op elsewhere (InMemory).
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT id FROM acct_fiscal_periods WHERE id = {0} FOR UPDATE", [period.Id], ct);
            await db.Entry(period).ReloadAsync(ct);
        }

        if (period.Status == target)
            throw new InvalidOperationException($"Fiscal period {periodId} is already {target}.");

        if (!Allowed[period.Status].Contains(target))
            throw new InvalidOperationException(
                $"Cannot transition fiscal period {periodId} from {period.Status} to {target}.");

        period.Status = target;

        // Close-transition audit: a close (→ SoftClosed/HardClosed) stamps ClosedBy/At; a reopen (→ Open)
        // stamps ReopenedBy/At.
        if (target == FiscalPeriodStatus.Open)
        {
            period.ReopenedByUserId = actorUserId;
            period.ReopenedAt = clock.UtcNow;
        }
        else
        {
            period.ClosedByUserId = actorUserId;
            period.ClosedAt = clock.UtcNow;
            // Auto-reversing accruals/prepaids (§12): reverse entries flagged AutoReverseNextPeriod into the
            // next period so the accrual doesn't linger. Idempotent (already-reversed entries are skipped).
            await ReverseAccrualsAsync(period, actorUserId, ct);
        }

        await db.SaveChangesAsync(ct); // Version token guards a concurrent status change

        return new FiscalPeriodModel(
            period.Id, period.FiscalYearId, period.PeriodNumber, period.Name,
            period.StartDate, period.EndDate, period.Status);
    }

    /// <summary>
    /// Reverses the period's <c>AutoReverseNextPeriod</c> entries (accruals/prepaids) into the next period
    /// (dated the day after this period ends). Only un-reversed originals are touched, so re-closing is a
    /// no-op. Requires a period to cover the reversal date — fails the close loudly if the next period isn't
    /// set up yet, rather than silently stranding the accrual.
    /// </summary>
    private async Task ReverseAccrualsAsync(FiscalPeriod period, int actorUserId, CancellationToken ct)
    {
        var accrualIds = await db.Set<JournalEntry>()
            .Where(e => e.FiscalPeriodId == period.Id
                && e.AutoReverseNextPeriod
                && e.Status == JournalEntryStatus.Posted
                && e.ReversalOfEntryId == null
                && e.ReversedByEntryId == null)
            .OrderBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (accrualIds.Count == 0)
            return;

        var reversalDate = period.EndDate.AddDays(1);

        var bookId = await db.FiscalYears
            .Where(y => y.Id == period.FiscalYearId)
            .Select(y => y.BookId)
            .FirstAsync(ct);

        var hasNextPeriod = await db.FiscalPeriods
            .Include(p => p.FiscalYear)
            .AnyAsync(p => p.FiscalYear.BookId == bookId
                && p.StartDate <= reversalDate && p.EndDate >= reversalDate, ct);

        if (!hasNextPeriod)
            throw new InvalidOperationException(
                $"Cannot close period {period.Name}: {accrualIds.Count} auto-reversing entr"
              + $"{(accrualIds.Count == 1 ? "y needs" : "ies need")} a period covering {reversalDate} to reverse "
              + "into — create the next period first.");

        foreach (var id in accrualIds)
            await postingEngine.ReverseAsync(
                id, reversalDate, $"Auto-reversal of accrual ({period.Name} close)", actorUserId, ct);
    }
}
