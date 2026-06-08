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

/// <summary>
/// Result of closing a job's production cost — the absorbed conversion costs and the variance swept.
/// <see cref="ProductionVariance"/> is the total WIP residual cleared; when a standard resolver is wired it is
/// decomposed into the named variance components (material usage + labor efficiency + overhead efficiency +
/// the unexplained <see cref="ProductionVarianceResidual"/> catch-all), which sum to it.
/// </summary>
public sealed record JobProductionCostCloseResult(
    int JobId, decimal LaborAbsorbed, decimal OverheadAbsorbed, decimal ProductionVariance, bool Posted)
{
    public decimal MaterialUsageVariance { get; init; }
    public decimal LaborRateVariance { get; init; }
    public decimal LaborEfficiencyVariance { get; init; }
    public decimal OverheadEfficiencyVariance { get; init; }
    public decimal ProductionVarianceResidual { get; init; }
}

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
    ISystemAuditWriter? auditWriter = null,
    // Standard costing: when wired, the WIP residual is decomposed into named variances (material usage, labor
    // efficiency, overhead efficiency) using the part standard-cost elements; null → a single lumped sweep.
    IStandardCostResolver? standardCost = null) : IProductionVariancePostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyLaborApplied = "LABOR_APPLIED";
    private const string KeyOverheadApplied = "OVERHEAD_APPLIED";
    private const string KeyProductionVariance = "PRODUCTION_VARIANCE";
    private const string KeyMaterialUsageVariance = "MATERIAL_USAGE_VARIANCE";
    private const string KeyLaborRateVariance = "LABOR_RATE_VARIANCE";
    private const string KeyLaborEfficiencyVariance = "LABOR_EFFICIENCY_VARIANCE";
    private const string KeyOverheadEfficiencyVariance = "OVERHEAD_EFFICIENCY_VARIANCE";

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

        // Labor at the STANDARD rate (efficiency basis) and at the ACTUAL rate (what's absorbed into WIP); the
        // difference is the labor RATE variance.
        var laborStd = Math.Round(await jobCost.GetActualLaborCostAsync(jobId, ct), 2);
        var laborRateVar = Math.Round(await jobCost.GetLaborRateVarianceAsync(jobId, ct), 2);
        var laborActual = Math.Round(await jobCost.GetActualLaborCostAtActualRateAsync(jobId, ct), 2);
        var burden = Math.Round(await jobCost.GetActualBurdenCostAsync(jobId, ct), 2);

        // ── 1) Absorb labor (at ACTUAL cost) + overhead into WIP (idempotent on the absorb key). ─────────────
        var absorbKey = $"{JournalSource.Inventory}:Job:{jobId}:WIPABSORB";
        var alreadyAbsorbed = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == absorbKey, ct);
        if (!alreadyAbsorbed && laborActual + burden > 0m)
        {
            var absorbLines = new List<PostingLine>
            {
                new() { AccountKey = KeyInventoryWip, Debit = laborActual + burden, JobId = jobId, Description = $"WIP — absorb labor + overhead (job {jobId})" },
            };
            if (laborActual > 0m)
                absorbLines.Add(new PostingLine { AccountKey = KeyLaborApplied, Credit = laborActual, Description = $"Labor absorbed — job {jobId}" });
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
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: false);

        var wipAccountId = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == book.Id && r.Key == KeyInventoryWip)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (wipAccountId is null)
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: false);

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
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: laborActual + burden > 0m);

        // Decompose the residual into named variances (material usage / labor rate + efficiency / overhead
        // efficiency) when a standard resolver is wired, with any unexplained remainder to PRODUCTION_VARIANCE;
        // else a single lumped sweep. Every branch zeroes the job's WIP (the named lines + WIP offset sum to
        // residual).
        decimal matUsage = 0m, laborEff = 0m, ohEff = 0m, catchall = residual;
        var lines = new List<PostingLine>();

        if (standardCost is not null)
        {
            // Standard cost of good output, decomposed, from the job's received production runs.
            var runs = await db.ProductionRuns.AsNoTracking()
                .Where(r => r.JobId == jobId && r.ReceivedToStockAt != null && r.ReceivedQuantity > 0)
                .Select(r => new { r.PartId, r.ReceivedQuantity })
                .ToListAsync(ct);
            decimal stdMat = 0m, stdLab = 0m, stdOh = 0m;
            foreach (var r in runs)
            {
                var e = await standardCost.ResolveAsync(r.PartId, ct);
                stdMat += e.Material * r.ReceivedQuantity;
                stdLab += e.Labor * r.ReceivedQuantity;
                stdOh += e.Overhead * r.ReceivedQuantity;
            }

            var actualMaterial = Math.Round(await jobCost.GetActualMaterialCostAsync(jobId, ct), 2);
            matUsage = Math.Round(actualMaterial - stdMat, 2);   // actual material vs standard for output
            laborEff = Math.Round(laborStd - stdLab, 2);         // actual hrs at STD rate vs standard → efficiency
            ohEff = Math.Round(burden - stdOh, 2);               // actual overhead vs standard → efficiency
            // labor rate variance (laborActual − laborStd) was computed up front; the labor portion of the
            // residual is (laborActual − stdLab) = laborRateVar + laborEff, so both post here.
            catchall = Math.Round(residual - matUsage - laborRateVar - laborEff - ohEff, 2);

            AddSignedVariance(lines, KeyMaterialUsageVariance, matUsage, jobId, "material usage");
            AddSignedVariance(lines, KeyLaborRateVariance, laborRateVar, jobId, "labor rate");
            AddSignedVariance(lines, KeyLaborEfficiencyVariance, laborEff, jobId, "labor efficiency");
            AddSignedVariance(lines, KeyOverheadEfficiencyVariance, ohEff, jobId, "overhead efficiency");
            AddSignedVariance(lines, KeyProductionVariance, catchall, jobId, "production (residual)");
        }
        else
        {
            AddSignedVariance(lines, KeyProductionVariance, residual, jobId, "production");
        }

        // WIP offset clears the residual to zero (the variances above net to `residual`).
        if (residual > 0m)
            lines.Add(new PostingLine { AccountKey = KeyInventoryWip, Credit = residual, JobId = jobId, Description = $"WIP clear — job {jobId}" });
        else
            lines.Add(new PostingLine { AccountKey = KeyInventoryWip, Debit = -residual, JobId = jobId, Description = $"WIP clear — job {jobId}" });

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
            Lines = lines,
        }, closedByUserId, ct);

        await TryAuditAsync(jobId, entry, laborActual, burden, residual, closedByUserId, ct);

        return new JobProductionCostCloseResult(jobId, laborActual, burden, residual, Posted: true)
        {
            MaterialUsageVariance = matUsage,
            LaborRateVariance = standardCost is not null ? laborRateVar : 0m,
            LaborEfficiencyVariance = laborEff,
            OverheadEfficiencyVariance = ohEff,
            ProductionVarianceResidual = catchall,
        };
    }

    /// <summary>Adds a variance line for a signed amount: positive = unfavorable (debit), negative = favorable
    /// (credit), zero = nothing. (Job dimension is carried only on the WIP offset, not the P&amp;L variance.)</summary>
    private static void AddSignedVariance(List<PostingLine> lines, string accountKey, decimal amount, int jobId, string label)
    {
        if (amount > 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, Debit = amount, Description = $"{label} variance (unfavorable) — job {jobId}" });
        else if (amount < 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, Credit = -amount, Description = $"{label} variance (favorable) — job {jobId}" });
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
