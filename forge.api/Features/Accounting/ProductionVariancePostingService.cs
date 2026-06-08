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

/// <summary>Result of closing a job's production cost — the absorbed conversion costs and the variance swept.</summary>
public sealed record JobProductionCostCloseResult(
    int JobId, decimal LaborAbsorbed, decimal OverheadAbsorbed, decimal ProductionVariance, bool Posted);

/// <summary>
/// Phase-2 STAGE E — job-cost close / production-variance recognition (the labor-+-overhead-aware completion
/// of the standard-costing loop). Material flows into GL WIP at issue and finished goods relieve WIP at
/// standard, but labor + overhead are tracked operationally (time entries) and never hit GL WIP — so the
/// standard FG relief over-relieves WIP by the conversion cost. This service closes a job's WIP in two steps,
/// <b>both gated by CAP-ACCT-FULLGL and idempotent</b>:
/// <list type="number">
///   <item><b>Absorb</b> the job's actual labor + burden into WIP: Dr INVENTORY_WIP (Job dim) / Cr
///         LABOR_APPLIED / Cr OVERHEAD_APPLIED. The applied accounts are contra-expense, netting against
///         actual wages / overhead at period end (over/under-absorbed).</item>
///   <item><b>Sweep</b> the job's remaining GL WIP balance (read by the Job dimension on the WIP lines —
///         material in + labor/OH absorbed − standard FG relieved) to PRODUCTION_VARIANCE, zeroing the job's
///         WIP. A debit residual is unfavorable (actual &gt; standard, Dr variance); a credit residual is
///         favorable (Cr variance).</item>
/// </list>
///
/// <para><b>Single lumped variance.</b> By reading the actual GL WIP-by-job balance and zeroing it, the sweep
/// captures every residual in one PRODUCTION_VARIANCE figure — material price/usage + labor/OH efficiency,
/// regardless of weighted-average-vs-unit-cost material differences. Decomposing it into named variances
/// (rate/efficiency/usage) is a future refinement. <b>Trigger:</b> an explicit close, run after all of the
/// job's production receipts; activity posted after the close is not re-swept (idempotent on the entry keys).
/// Subcontract conversion is not yet absorbed here (a documented follow-up).</para>
/// </summary>
public interface IProductionVariancePostingService
{
    Task<JobProductionCostCloseResult> CloseJobProductionCostAsync(
        int jobId, DateOnly entryDate, int closedByUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ProductionVariancePostingService(
    AppDbContext db,
    IPostingEngine postingEngine,
    ICapabilitySnapshotProvider capabilities,
    IJobCostService jobCost,
    ISystemAuditWriter? auditWriter = null) : IProductionVariancePostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyLaborApplied = "LABOR_APPLIED";
    private const string KeyOverheadApplied = "OVERHEAD_APPLIED";
    private const string KeyProductionVariance = "PRODUCTION_VARIANCE";

    public async Task<JobProductionCostCloseResult> CloseJobProductionCostAsync(
        int jobId, DateOnly entryDate, int closedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return new JobProductionCostCloseResult(jobId, 0m, 0m, 0m, Posted: false);

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            throw new PostingException(
                "NO_POSTING_BOOK",
                "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to close job production cost into.");

        var labor = Math.Round(await jobCost.GetActualLaborCostAsync(jobId, ct), 2);
        var burden = Math.Round(await jobCost.GetActualBurdenCostAsync(jobId, ct), 2);

        // ── 1) Absorb labor + overhead into WIP (idempotent on the absorb key). ────────────────────────────
        var absorbKey = $"{JournalSource.Inventory}:Job:{jobId}:WIPABSORB";
        var alreadyAbsorbed = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == absorbKey, ct);
        if (!alreadyAbsorbed && labor + burden > 0m)
        {
            var absorbLines = new List<PostingLine>
            {
                new() { AccountKey = KeyInventoryWip, Debit = labor + burden, JobId = jobId, Description = $"WIP — absorb labor + overhead (job {jobId})" },
            };
            if (labor > 0m)
                absorbLines.Add(new PostingLine { AccountKey = KeyLaborApplied, Credit = labor, Description = $"Labor absorbed — job {jobId}" });
            if (burden > 0m)
                absorbLines.Add(new PostingLine { AccountKey = KeyOverheadApplied, Credit = burden, Description = $"Overhead absorbed — job {jobId}" });

            await postingEngine.PostAsync(new PostingRequest
            {
                BookId = book.Id,
                EntryDate = entryDate,
                Source = JournalSource.Inventory,
                SourceType = "Job",
                SourceId = jobId,
                CurrencyId = book.FunctionalCurrencyId,
                Memo = $"WIP absorption — labor + overhead, job {jobId}",
                IdempotencyKey = absorbKey,
                Lines = absorbLines,
            }, closedByUserId, ct);
        }

        // ── 2) Sweep the job's remaining GL WIP balance to PRODUCTION_VARIANCE (idempotent on the var key). ──
        var varKey = $"{JournalSource.Inventory}:Job:{jobId}:PRODVARIANCE";
        var alreadyVarianced = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == varKey, ct);
        if (alreadyVarianced)
            return new JobProductionCostCloseResult(jobId, labor, burden, 0m, Posted: false);

        var wipAccountId = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == book.Id && r.Key == KeyInventoryWip)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (wipAccountId is null)
            return new JobProductionCostCloseResult(jobId, labor, burden, 0m, Posted: false);

        // GL WIP balance for this job (debit-positive): material in + labor/OH absorbed − standard FG relieved.
        var wipBalance = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == book.Id
                 && line.GlAccountId == wipAccountId.Value
                 && line.JobId == jobId
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             select line.Debit - line.Credit)
            .SumAsync(ct);

        var residual = Math.Round(wipBalance, 2);
        if (residual == 0m)
            return new JobProductionCostCloseResult(jobId, labor, burden, 0m, Posted: labor + burden > 0m);

        // Unfavorable (debit residual — actual exceeded standard): Dr PRODUCTION_VARIANCE / Cr WIP.
        // Favorable (credit residual — over-relieved): Dr WIP / Cr PRODUCTION_VARIANCE.
        var varianceLines = residual > 0m
            ?
            [
                new PostingLine { AccountKey = KeyProductionVariance, Debit = residual, Description = $"Production variance (unfavorable) — job {jobId}" },
                new PostingLine { AccountKey = KeyInventoryWip, Credit = residual, JobId = jobId, Description = $"WIP clear — job {jobId}" },
            ]
            : new List<PostingLine>
            {
                new() { AccountKey = KeyInventoryWip, Debit = -residual, JobId = jobId, Description = $"WIP clear — job {jobId}" },
                new() { AccountKey = KeyProductionVariance, Credit = -residual, Description = $"Production variance (favorable) — job {jobId}" },
            };

        var entry = await postingEngine.PostAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "Job",
            SourceId = jobId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Production variance — job {jobId}",
            IdempotencyKey = varKey,
            Lines = varianceLines,
        }, closedByUserId, ct);

        await TryAuditAsync(jobId, entry, labor, burden, residual, closedByUserId, ct);

        return new JobProductionCostCloseResult(jobId, labor, burden, residual, Posted: true);
    }

    private async Task TryAuditAsync(
        int jobId, JournalEntry entry, decimal labor, decimal burden, decimal variance, int actorUserId, CancellationToken ct)
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
                    jobId,
                    laborAbsorbed = labor.ToString(CultureInfo.InvariantCulture),
                    overheadAbsorbed = burden.ToString(CultureInfo.InvariantCulture),
                    productionVariance = variance.ToString(CultureInfo.InvariantCulture),
                },
                reason = $"Job {jobId} production-cost close — labor/overhead absorbed, WIP residual swept to variance.",
            });

            await auditWriter.WriteAsync(
                action: "GlProductionVariancePosted",
                userId: actorUserId,
                entityType: nameof(JournalEntry),
                entityId: null,
                details: details,
                ct: ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "Production-variance posting audit write failed for job {JobId} (entry {EntryId}); posting itself is committed.",
                jobId, entry.Id);
        }
    }
}
