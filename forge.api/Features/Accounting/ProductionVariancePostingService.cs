using System.Globalization;
using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Terminology;
using Forge.Api.Services;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
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
    public decimal SubcontractAbsorbed { get; init; }
    public decimal MaterialUsageVariance { get; init; }
    public decimal LaborRateVariance { get; init; }
    public decimal LaborEfficiencyVariance { get; init; }
    public decimal OverheadEfficiencyVariance { get; init; }
    public decimal ProductionVarianceResidual { get; init; }
    public decimal CostOfGoodsSold { get; init; }
    public int? WriteOffGlAccountId { get; init; }
}

/// <summary>
/// Phase-2 STAGE E — job-cost close / production-variance recognition (the labor-+-overhead-aware completion
/// of the standard-costing loop). Material flows into GL WIP at issue and finished goods relieve WIP at
/// standard, but labor + overhead are tracked operationally (time entries) — so without absorption the standard
/// FG relief over-relieves WIP by the conversion cost. Each production receipt first absorbs the conversion cost
/// incurred so far (<see cref="IProductionVariancePostingService.AbsorbConversionToDateAsync"/>), so an open
/// job's WIP does not go negative. This service closes a job's WIP in two steps, <b>both gated by
/// CAP-ACCT-FULLGL and idempotent</b>:
/// <list type="number">
///   <item><b>Absorb</b> the job's actual labor + burden not yet absorbed by its receipts into WIP: Dr
///         INVENTORY_WIP (Job dim) / Cr LABOR_APPLIED / Cr OVERHEAD_APPLIED. The applied accounts are
///         contra-expense, netting against actual wages / overhead at period end (over/under-absorbed).</item>
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
/// Subcontract conversion (outside-vendor operation steps) is absorbed into WIP alongside labor + overhead
/// (Dr INVENTORY_WIP / Cr SUBCONTRACT_APPLIED) — like them it is tracked operationally and never hits GL WIP,
/// so the standard FG relief would otherwise over-relieve by it.</para>
/// </summary>
public interface IProductionVariancePostingService
{
    Task<JobProductionCostCloseResult> CloseJobProductionCostAsync(
        int jobId, DateOnly entryDate, int closedByUserId, CancellationToken ct = default);

    /// <summary>
    /// The same close as <see cref="CloseJobProductionCostAsync"/>, run by a job disposition (added to inventory or
    /// scrapped) rather than the accounting close: it is the business action, so both entries post on the
    /// sub-ledger path (<see cref="SubledgerPosting"/>). Idempotent with the explicit close on the same entry keys.
    /// </summary>
    Task<JobProductionCostCloseResult> CloseDisposedJobProductionCostAsync(
        int jobId, DateOnly entryDate, int disposedByUserId, CancellationToken ct = default);

    /// <summary>
    /// The disposition close for a job that never reaches stock (<see cref="DisposedJobCostTarget"/>): absorbs its
    /// conversion cost as every close does, then relieves the whole WIP balance at actual. Shipped to the customer,
    /// cost of goods sold takes the standard cost of the shipped quantity and the difference goes to the named
    /// variances, as on the inventory path (with no standard, cost of goods sold takes it all); any other
    /// disposition writes it off to the chosen expense account or production variance. Same entry keys as the
    /// other closes, so a later close does not sweep the job again.
    /// </summary>
    Task<JobProductionCostCloseResult> CloseDisposedJobProductionCostAsync(
        int jobId, DateOnly entryDate, int disposedByUserId, DisposedJobCostTarget target, CancellationToken ct = default);

    /// <summary>
    /// Absorbs the job's labor, overhead and subcontract cost incurred so far, less what earlier receipts and
    /// closes already absorbed, into WIP ahead of a production receipt (Dr INVENTORY_WIP / Cr *_APPLIED, posted
    /// as the system principal). Returns the amount absorbed; zero while CAP-ACCT-FULLGL is off, when nothing
    /// new has been incurred, or when the run was already absorbed.
    /// </summary>
    Task<decimal> AbsorbConversionToDateAsync(
        int jobId, int productionRunId, DateOnly entryDate, int actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Capitalizes a job built as a company asset: absorbs its labor + overhead + subcontract into WIP (the same
    /// absorb entry the close posts, so the two never double-absorb), then moves the job's whole GL WIP balance to
    /// a fixed-asset account (Dr <paramref name="assetGlAccountId"/>, else the book's FIXED_ASSETS account / Cr
    /// INVENTORY_WIP), leaving nothing for a later close to sweep to variance. It is the job-disposition business
    /// action, so both entries post on the sub-ledger path (<see cref="SubledgerPosting"/>). No-op while
    /// CAP-ACCT-FULLGL is off; a validation error when the job carries WIP and no asset account resolves.
    /// </summary>
    Task<JobCapitalizationResult> CapitalizeJobCostAsync(
        int jobId, int? assetGlAccountId, DateOnly entryDate, int capitalizedByUserId, CancellationToken ct = default);
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
    IStandardCostResolver? standardCost = null,
    ITerminologyLabels? terminology = null) : IProductionVariancePostingService
{
    private const string FullGlCapability = "CAP-ACCT-FULLGL";
    private const string KeyInventoryWip = "INVENTORY_WIP";
    private const string KeyLaborApplied = "LABOR_APPLIED";
    private const string KeyOverheadApplied = "OVERHEAD_APPLIED";
    private const string KeySubcontractApplied = "SUBCONTRACT_APPLIED";
    private const string KeyProductionVariance = "PRODUCTION_VARIANCE";
    private const string KeyMaterialUsageVariance = "MATERIAL_USAGE_VARIANCE";
    private const string KeyLaborRateVariance = "LABOR_RATE_VARIANCE";
    private const string KeyLaborEfficiencyVariance = "LABOR_EFFICIENCY_VARIANCE";
    private const string KeyOverheadEfficiencyVariance = "OVERHEAD_EFFICIENCY_VARIANCE";
    private const string KeyCogs = "COGS";
    public const string KeyFixedAssets = "FIXED_ASSETS";

    private static readonly AccountType[] CapitalAccountTypes = [AccountType.Asset];
    private static readonly AccountType[] WriteOffAccountTypes = [AccountType.Expense];

    public Task<JobProductionCostCloseResult> CloseJobProductionCostAsync(
        int jobId, DateOnly entryDate, int closedByUserId, CancellationToken ct = default) =>
        CloseAsync(jobId, entryDate, closedByUserId, asSystem: false, target: null, ct);

    public Task<JobProductionCostCloseResult> CloseDisposedJobProductionCostAsync(
        int jobId, DateOnly entryDate, int disposedByUserId, CancellationToken ct = default) =>
        CloseAsync(jobId, entryDate, disposedByUserId, asSystem: true, target: null, ct);

    public Task<JobProductionCostCloseResult> CloseDisposedJobProductionCostAsync(
        int jobId, DateOnly entryDate, int disposedByUserId, DisposedJobCostTarget target, CancellationToken ct = default) =>
        CloseAsync(jobId, entryDate, disposedByUserId, asSystem: true, target, ct);

    private async Task<JobProductionCostCloseResult> CloseAsync(
        int jobId, DateOnly entryDate, int closedByUserId, bool asSystem, DisposedJobCostTarget? target, CancellationToken ct)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return new JobProductionCostCloseResult(jobId, 0m, 0m, 0m, Posted: false);

        var book = await RequireBookAsync(ct);
        var writeOffAccount = target is { Disposition: JobDisposition.Other, ExpenseGlAccountId: int writeOffId }
            ? await PostableGlAccountLookup.ResolveAsync(
                db, book.Id, writeOffId, null, "Data.ExpenseGlAccountId", WriteOffAccountTypes, ct)
            : null;
        var jobLabel = await JobMemoLabel.ResolveAsync(db, terminology, jobId, ct);
        var (laborStd, laborRateVar, laborActual, burden, subcontract) = await AbsorbAsync(book, jobId, jobLabel, entryDate, closedByUserId, asSystem, ct);

        // ── 2) Sweep the job's remaining GL WIP balance to PRODUCTION_VARIANCE (idempotent on the var key). ──
        var varKey = $"{JournalSource.Inventory}:Job:{jobId}:PRODVARIANCE";
        var alreadyVarianced = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == varKey, ct);
        if (alreadyVarianced)
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: false) { SubcontractAbsorbed = subcontract };

        var wipAccountId = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == book.Id && r.Key == KeyInventoryWip)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (wipAccountId is null)
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: false) { SubcontractAbsorbed = subcontract };

        var residual = await GetJobWipBalanceAsync(book.Id, wipAccountId.Value, jobId, ct);
        if (residual == 0m && (standardCost is null || await ReceiptVarianceAsync(book.Id, jobId, ct) == 0m))
            return new JobProductionCostCloseResult(jobId, laborActual, burden, 0m, Posted: laborActual + burden + subcontract > 0m) { SubcontractAbsorbed = subcontract };

        // Decompose the residual into named variances (material usage / labor rate + efficiency / overhead
        // efficiency) when a standard resolver is wired, with any unexplained remainder to PRODUCTION_VARIANCE;
        // else a single lumped sweep. Every branch zeroes the job's WIP (the named lines + WIP offset sum to
        // residual).
        decimal matUsage = 0m, laborEff = 0m, ohEff = 0m, catchall = residual, cogs = 0m;
        var lines = new List<PostingLine>();
        var postsRateVariance = standardCost is not null;

        if (target?.Disposition == JobDisposition.ShipToCustomer)
        {
            var standard = await ShippedStandardAsync(target, ct);
            if (standard.Total > 0m)
            {
                var received = await ReceivedStandardAsync(jobId, ct);
                var actualMaterial = Math.Round(await jobCost.GetActualMaterialCostAsync(jobId, ct), 2);
                cogs = Math.Round(standard.Material, 2) + Math.Round(standard.Labor, 2) + Math.Round(standard.Overhead, 2);
                matUsage = Math.Round(actualMaterial - received.Material - standard.Material, 2);
                laborEff = Math.Round(laborStd - received.Labor - standard.Labor, 2);
                ohEff = Math.Round(burden - received.Overhead - standard.Overhead, 2);
                catchall = Math.Round(residual - cogs - matUsage - laborRateVar - laborEff - ohEff, 2);

                AddSigned(lines, KeyCogs, null, cogs, $"Cost of goods sold at standard — {jobLabel} shipped to the customer");
                AddSignedVariance(lines, KeyMaterialUsageVariance, matUsage, jobLabel, "material usage");
                AddSignedVariance(lines, KeyLaborRateVariance, laborRateVar, jobLabel, "labor rate");
                AddSignedVariance(lines, KeyLaborEfficiencyVariance, laborEff, jobLabel, "labor efficiency");
                AddSignedVariance(lines, KeyOverheadEfficiencyVariance, ohEff, jobLabel, "overhead efficiency");
                AddSignedVariance(lines, KeyProductionVariance, catchall, jobLabel, "production (residual)");
                postsRateVariance = true;
            }
            else
            {
                cogs = residual;
                catchall = 0m;
                postsRateVariance = false;
                AddSigned(lines, KeyCogs, null, residual, $"Cost of goods sold — {jobLabel} shipped to the customer");
            }
        }
        else if (target?.Disposition == JobDisposition.Other)
        {
            postsRateVariance = false;
            if (writeOffAccount is not null)
            {
                catchall = 0m;
                AddSigned(lines, null, writeOffAccount.Id, residual, $"Work in process written off — {jobLabel}");
            }
            else
            {
                AddSignedVariance(lines, KeyProductionVariance, residual, jobLabel, "production (written off)");
            }
        }
        else if (standardCost is not null)
        {
            // Standard cost of good output, decomposed, from the job's received production runs.
            var (stdMat, stdLab, stdOh) = await ReceivedStandardAsync(jobId, ct);

            var actualMaterial = Math.Round(await jobCost.GetActualMaterialCostAsync(jobId, ct), 2);
            matUsage = Math.Round(actualMaterial - stdMat, 2);   // actual material vs standard for output
            laborEff = Math.Round(laborStd - stdLab, 2);         // actual hrs at STD rate vs standard → efficiency
            ohEff = Math.Round(burden - stdOh, 2);               // actual overhead vs standard → efficiency
            // labor rate variance (laborActual − laborStd) was computed up front; the labor portion of the
            // residual is (laborActual − stdLab) = laborRateVar + laborEff, so both post here.
            catchall = Math.Round(residual - matUsage - laborRateVar - laborEff - ohEff, 2);

            AddSignedVariance(lines, KeyMaterialUsageVariance, matUsage, jobLabel, "material usage");
            AddSignedVariance(lines, KeyLaborRateVariance, laborRateVar, jobLabel, "labor rate");
            AddSignedVariance(lines, KeyLaborEfficiencyVariance, laborEff, jobLabel, "labor efficiency");
            AddSignedVariance(lines, KeyOverheadEfficiencyVariance, ohEff, jobLabel, "overhead efficiency");
            AddSignedVariance(lines, KeyProductionVariance, catchall, jobLabel, "production (residual)");
        }
        else
        {
            AddSignedVariance(lines, KeyProductionVariance, residual, jobLabel, "production");
        }

        // WIP offset clears the residual to zero (the variances above net to `residual`).
        if (residual > 0m)
            lines.Add(new PostingLine { AccountKey = KeyInventoryWip, Credit = residual, JobId = jobId, Description = $"WIP clear — {jobLabel}" });
        else if (residual < 0m)
            lines.Add(new PostingLine { AccountKey = KeyInventoryWip, Debit = -residual, JobId = jobId, Description = $"WIP clear — {jobLabel}" });

        var varianceRequest = new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "Job",
            SourceId = jobId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = target?.Disposition switch
            {
                JobDisposition.ShipToCustomer => $"Cost of goods sold — {jobLabel} shipped to the customer",
                JobDisposition.Other => $"Work in process written off — {jobLabel}",
                _ => $"Production variance — {jobLabel}",
            },
            IdempotencyKey = varKey,
            Lines = lines,
        };
        var entry = asSystem
            ? await postingEngine.PostAsSystemAsync(varianceRequest, closedByUserId, ct)
            : await postingEngine.PostAsync(varianceRequest, closedByUserId, ct);

        await TryAuditAsync(jobId, jobLabel, entry, laborActual, burden, residual, closedByUserId, ct);

        return new JobProductionCostCloseResult(jobId, laborActual, burden, residual, Posted: true)
        {
            SubcontractAbsorbed = subcontract,
            MaterialUsageVariance = matUsage,
            LaborRateVariance = postsRateVariance ? laborRateVar : 0m,
            LaborEfficiencyVariance = laborEff,
            OverheadEfficiencyVariance = ohEff,
            ProductionVarianceResidual = catchall,
            CostOfGoodsSold = cogs,
            WriteOffGlAccountId = writeOffAccount?.Id,
        };
    }

    private async Task<StandardCostElements> ReceivedStandardAsync(int jobId, CancellationToken ct)
    {
        if (standardCost is null)
            return StandardCostElements.Zero;

        var runs = await db.ProductionRuns.AsNoTracking()
            .Where(r => r.JobId == jobId && r.ReceivedToStockAt != null && r.ReceivedQuantity > 0)
            .Select(r => new { r.PartId, r.ReceivedQuantity })
            .ToListAsync(ct);
        decimal material = 0m, labor = 0m, overhead = 0m;
        foreach (var r in runs)
        {
            var e = await standardCost.ResolveAsync(r.PartId, ct);
            material += e.Material * r.ReceivedQuantity;
            labor += e.Labor * r.ReceivedQuantity;
            overhead += e.Overhead * r.ReceivedQuantity;
        }
        return new StandardCostElements(material, labor, overhead);
    }

    private async Task<StandardCostElements> ShippedStandardAsync(DisposedJobCostTarget target, CancellationToken ct)
    {
        if (standardCost is null || target.PartId is not int partId || target.ShippedQuantity <= 0m)
            return StandardCostElements.Zero;

        var unit = await standardCost.ResolveAsync(partId, ct);
        return new StandardCostElements(
            unit.Material * target.ShippedQuantity, unit.Labor * target.ShippedQuantity, unit.Overhead * target.ShippedQuantity);
    }

    private static void AddSigned(List<PostingLine> lines, string? accountKey, int? glAccountId, decimal amount, string description)
    {
        if (amount > 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, GlAccountId = glAccountId, Debit = amount, Description = description });
        else if (amount < 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, GlAccountId = glAccountId, Credit = -amount, Description = description });
    }

    public async Task<decimal> AbsorbConversionToDateAsync(
        int jobId, int productionRunId, DateOnly entryDate, int actingUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return 0m;

        var book = await db.Books.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
        if (book is null)
            return 0m;

        var runKey = $"{AbsorbKey(jobId)}:Run:{productionRunId}";
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == runKey, ct))
            return 0m;

        await jobCost.PriceUnpricedTimeEntriesAsync(jobId, ct);
        var laborActual = Math.Round(await jobCost.GetActualLaborCostAtActualRateAsync(jobId, ct), 2);
        var burden = Math.Round(await jobCost.GetActualBurdenCostAsync(jobId, ct), 2);
        var subcontract = Math.Round(await jobCost.GetActualSubcontractCostAsync(jobId, ct), 2);
        var absorbed = await AbsorbedToDateAsync(book.Id, jobId, ct);
        var labor = Delta(laborActual, absorbed.Labor);
        var overhead = Delta(burden, absorbed.Overhead);
        var sub = Delta(subcontract, absorbed.Subcontract);

        var jobLabel = await JobMemoLabel.ResolveAsync(db, terminology, jobId, ct);
        var request = AbsorptionRequest(book, jobId, jobLabel, runKey, entryDate, labor, overhead, sub);
        if (request is null)
            return 0m;

        var neededKeys = request.Lines.Select(l => l.AccountKey).Distinct().ToList();
        var mappedKeys = await db.Set<AccountDeterminationRule>().AsNoTracking()
            .Where(r => r.BookId == book.Id && neededKeys.Contains(r.Key))
            .Select(r => r.Key)
            .Distinct()
            .CountAsync(ct);
        if (mappedKeys < neededKeys.Count)
        {
            Log.Warning(
                "Receipt-time WIP absorption skipped for job {JobId} run {RunId}: book {BookId} is missing a determination rule for {Keys}.",
                jobId, productionRunId, book.Id, string.Join(", ", neededKeys));
            return 0m;
        }

        await postingEngine.PostAsSystemAsync(request, actingUserId, ct);
        return labor + overhead + sub;
    }

    private static string AbsorbKey(int jobId) => $"{JournalSource.Inventory}:Job:{jobId}:WIPABSORB";

    private static decimal Delta(decimal toDate, decimal absorbed) => Math.Max(0m, Math.Round(toDate - absorbed, 2));

    /// <summary>What the job's receipts and close have already absorbed into WIP, per applied account: the
    /// posted credits on LABOR_APPLIED / OVERHEAD_APPLIED / SUBCONTRACT_APPLIED under the job's absorb key and its
    /// per-run keys.</summary>
    private async Task<(decimal Labor, decimal Overhead, decimal Subcontract)> AbsorbedToDateAsync(
        int bookId, int jobId, CancellationToken ct)
    {
        var key = AbsorbKey(jobId);
        var runPrefix = $"{key}:";
        var accounts = await db.Set<AccountDeterminationRule>().AsNoTracking()
            .Where(r => r.BookId == bookId
                && (r.Key == KeyLaborApplied || r.Key == KeyOverheadApplied || r.Key == KeySubcontractApplied))
            .Select(r => new { r.Key, r.GlAccountId })
            .ToListAsync(ct);
        var accountIds = accounts.Select(a => a.GlAccountId).ToList();

        var credits = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && je.Status == JournalEntryStatus.Posted
                 && je.IdempotencyKey != null
                 && (je.IdempotencyKey == key || je.IdempotencyKey.StartsWith(runPrefix))
                 && accountIds.Contains(line.GlAccountId)
             group line by line.GlAccountId into g
             select new { GlAccountId = g.Key, Amount = g.Sum(l => l.Credit - l.Debit) })
            .ToListAsync(ct);

        decimal SumFor(string accountKey) => credits
            .Where(c => accounts.Any(a => a.Key == accountKey && a.GlAccountId == c.GlAccountId))
            .Sum(c => c.Amount);

        return (SumFor(KeyLaborApplied), SumFor(KeyOverheadApplied), SumFor(KeySubcontractApplied));
    }

    private static PostingRequest? AbsorptionRequest(
        Book book, int jobId, string jobLabel, string idempotencyKey, DateOnly entryDate, decimal labor, decimal overhead, decimal subcontract)
    {
        var total = labor + overhead + subcontract;
        if (total <= 0m)
            return null;

        var lines = new List<PostingLine>
        {
            new() { AccountKey = KeyInventoryWip, Debit = total, JobId = jobId, Description = $"WIP — absorb labor + overhead + subcontract ({jobLabel})" },
        };
        if (labor > 0m)
            lines.Add(new PostingLine { AccountKey = KeyLaborApplied, Credit = labor, Description = $"Labor absorbed — {jobLabel}" });
        if (overhead > 0m)
            lines.Add(new PostingLine { AccountKey = KeyOverheadApplied, Credit = overhead, Description = $"Overhead absorbed — {jobLabel}" });
        if (subcontract > 0m)
            lines.Add(new PostingLine { AccountKey = KeySubcontractApplied, Credit = subcontract, Description = $"Subcontract absorbed — {jobLabel}" });

        return new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "Job",
            SourceId = jobId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"WIP absorption — labor + overhead + subcontract, {jobLabel}",
            IdempotencyKey = idempotencyKey,
            Lines = lines,
        };
    }

    public async Task<JobCapitalizationResult> CapitalizeJobCostAsync(
        int jobId, int? assetGlAccountId, DateOnly entryDate, int capitalizedByUserId, CancellationToken ct = default)
    {
        if (!capabilities.IsEnabled(FullGlCapability))
            return new JobCapitalizationResult(jobId, 0m, 0m, 0m, 0m, null, Posted: false);

        var book = await RequireBookAsync(ct);
        var explicitAccount = await PostableGlAccountLookup.ResolveAsync(
            db, book.Id, assetGlAccountId, null, "Data.AssetGlAccountId", CapitalAccountTypes, ct);

        var capKey = $"{JournalSource.Inventory}:Job:{jobId}:CAPITALIZE";
        if (await db.JournalEntries.IgnoreQueryFilters().AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == capKey, ct))
            return new JobCapitalizationResult(jobId, 0m, 0m, 0m, 0m, null, Posted: false);

        var jobLabel = await JobMemoLabel.ResolveAsync(db, terminology, jobId, ct);
        var (_, _, laborActual, burden, subcontract) = await AbsorbAsync(book, jobId, jobLabel, entryDate, capitalizedByUserId, asSystem: true, ct);
        var absorbedAny = laborActual + burden + subcontract > 0m;

        var wipAccountId = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == book.Id && r.Key == KeyInventoryWip)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        var balance = wipAccountId is null ? 0m : await GetJobWipBalanceAsync(book.Id, wipAccountId.Value, jobId, ct);
        if (balance <= 0m)
            return new JobCapitalizationResult(jobId, laborActual, burden, subcontract, 0m, null, Posted: absorbedAny);

        var assetAccount = explicitAccount
            ?? await PostableGlAccountLookup.ResolveAsync(db, book.Id, null, KeyFixedAssets, "Data.AssetGlAccountId", CapitalAccountTypes, ct)
            ?? throw new ValidationException([new ValidationFailure("Data.AssetGlAccountId",
                $"{jobLabel} carries {balance:N2} of work in process. Choose the fixed-asset GL account to capitalize it into.")]);

        await postingEngine.PostAsSystemAsync(new PostingRequest
        {
            BookId = book.Id,
            EntryDate = entryDate,
            Source = JournalSource.Inventory,
            SourceType = "Job",
            SourceId = jobId,
            CurrencyId = book.FunctionalCurrencyId,
            Memo = $"Capitalize {jobLabel} as a fixed asset",
            IdempotencyKey = capKey,
            Lines =
            [
                new PostingLine { GlAccountId = assetAccount.Id, Debit = balance, Description = $"Fixed asset — capitalized from {jobLabel}" },
                new PostingLine { AccountKey = KeyInventoryWip, Credit = balance, JobId = jobId, Description = $"WIP relieved — {jobLabel} capitalized" },
            ],
        }, capitalizedByUserId, ct);

        return new JobCapitalizationResult(jobId, laborActual, burden, subcontract, balance, assetAccount.Id, Posted: true);
    }

    private async Task<Book> RequireBookAsync(CancellationToken ct) =>
        await db.Books.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).FirstOrDefaultAsync(ct)
        ?? throw new PostingException(
            "NO_POSTING_BOOK",
            "CAP-ACCT-FULLGL is enabled but no active accounting Book is seeded to close job production cost into.");

    private async Task<(decimal LaborStd, decimal LaborRateVar, decimal LaborActual, decimal Burden, decimal Subcontract)> AbsorbAsync(
        Book book, int jobId, string jobLabel, DateOnly entryDate, int userId, bool asSystem, CancellationToken ct)
    {
        // Labor at the STANDARD rate (efficiency basis) and at the ACTUAL rate (what's absorbed into WIP); the
        // difference is the labor RATE variance.
        await jobCost.PriceUnpricedTimeEntriesAsync(jobId, ct);
        var laborStd = Math.Round(await jobCost.GetActualLaborCostAsync(jobId, ct), 2);
        var laborRateVar = Math.Round(await jobCost.GetLaborRateVarianceAsync(jobId, ct), 2);
        var laborActual = Math.Round(await jobCost.GetActualLaborCostAtActualRateAsync(jobId, ct), 2);
        var burden = Math.Round(await jobCost.GetActualBurdenCostAsync(jobId, ct), 2);
        // Subcontract conversion (outside-vendor operation steps) is tracked operationally and — like labor and
        // overhead — never hits GL WIP, so the standard FG relief over-relieves by it too. Absorb it the same way.
        var subcontract = Math.Round(await jobCost.GetActualSubcontractCostAsync(jobId, ct), 2);

        // ── 1) Absorb labor (at ACTUAL cost) + overhead + subcontract into WIP (idempotent on the absorb key). ─
        var absorbKey = AbsorbKey(jobId);
        var alreadyAbsorbed = await db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.BookId == book.Id && e.IdempotencyKey == absorbKey, ct);
        if (!alreadyAbsorbed)
        {
            var absorbed = await AbsorbedToDateAsync(book.Id, jobId, ct);
            var request = AbsorptionRequest(
                book, jobId, jobLabel, absorbKey, entryDate,
                Delta(laborActual, absorbed.Labor), Delta(burden, absorbed.Overhead), Delta(subcontract, absorbed.Subcontract));
            if (request is not null)
            {
                if (asSystem)
                    await postingEngine.PostAsSystemAsync(request, userId, ct);
                else
                    await postingEngine.PostAsync(request, userId, ct);
            }
        }

        return (laborStd, laborRateVar, laborActual, burden, subcontract);
    }

    /// <summary>The favorable production variance the job's receipts credited when the standard FG value exceeded
    /// the job's WIP (net credit on PRODUCTION_VARIANCE from its ProductionRun receipt entries).</summary>
    private async Task<decimal> ReceiptVarianceAsync(int bookId, int jobId, CancellationToken ct)
    {
        var varianceAccountId = await db.Set<AccountDeterminationRule>()
            .Where(r => r.BookId == bookId && r.Key == KeyProductionVariance)
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);
        if (varianceAccountId is null)
            return 0m;

        var runIds = db.ProductionRuns.IgnoreQueryFilters().Where(r => r.JobId == jobId).Select(r => (long?)r.Id);
        var variance = await
            (from line in db.JournalLines.IgnoreQueryFilters()
             join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
             where je.BookId == bookId
                 && je.SourceType == "ProductionRun"
                 && runIds.Contains(je.SourceId)
                 && line.GlAccountId == varianceAccountId.Value
                 && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
             select line.Credit - line.Debit)
            .SumAsync(ct);

        return Math.Round(variance, 2);
    }

    private Task<decimal> GetJobWipBalanceAsync(int bookId, int wipAccountId, int jobId, CancellationToken ct) =>
        JobWipLedger.BalanceAsync(db, bookId, wipAccountId, jobId, ct);

    /// <summary>Adds a variance line for a signed amount: positive = unfavorable (debit), negative = favorable
    /// (credit), zero = nothing. (Job dimension is carried only on the WIP offset, not the P&amp;L variance.)</summary>
    private static void AddSignedVariance(List<PostingLine> lines, string accountKey, decimal amount, string jobLabel, string label)
    {
        if (amount > 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, Debit = amount, Description = $"{label} variance (unfavorable) — {jobLabel}" });
        else if (amount < 0m)
            lines.Add(new PostingLine { AccountKey = accountKey, Credit = -amount, Description = $"{label} variance (favorable) — {jobLabel}" });
    }

    private async Task TryAuditAsync(
        int jobId, string jobLabel, JournalEntry entry, decimal labor, decimal burden, decimal variance, int actorUserId, CancellationToken ct)
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
                reason = $"{jobLabel} production-cost close — labor/overhead absorbed, WIP residual swept to variance.",
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
