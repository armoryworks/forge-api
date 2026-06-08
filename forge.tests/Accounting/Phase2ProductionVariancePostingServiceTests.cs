using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE E — job-cost close / production variance. Proves: DARK by default; closing a job absorbs its
/// actual labor + overhead into WIP (Dr WIP / Cr LABOR_APPLIED / Cr OVERHEAD_APPLIED) and sweeps the remaining
/// GL WIP-by-job balance to PRODUCTION_VARIANCE (unfavorable Dr / favorable Cr), zeroing the job's WIP;
/// idempotent.
/// </summary>
public class Phase2ProductionVariancePostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int RawId = 130;
    private const int WipId = 131;
    private const int FgId = 132;
    private const int LaborAppliedId = 140;
    private const int OhAppliedId = 141;
    private const int ProdVarId = 142;
    private const int MatUsageId = 143;
    private const int LaborEffId = 144;
    private const int OhEffId = 145;
    private const int LaborRateId = 146;

    private const int JobId = 42;
    private static readonly DateOnly EntryDate = new(2026, 1, 15);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static ProductionVariancePostingService Service(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), new JobCostService(db));

    private static ProductionVariancePostingService ServiceWithStd(AppDbContext db)
        => new(db, Engine(db), new FakeCapabilities(true), new JobCostService(db),
            auditWriter: null, standardCost: new StandardCostResolver(db));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = RawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = WipId, BookId = BookId, AccountNumber = "13200", Name = "Inventory — WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = FgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — FG", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = LaborAppliedId, BookId = BookId, AccountNumber = "51210", Name = "Labor Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OhAppliedId, BookId = BookId, AccountNumber = "51220", Name = "Overhead Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ProdVarId, BookId = BookId, AccountNumber = "51200", Name = "Production Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = MatUsageId, BookId = BookId, AccountNumber = "51100", Name = "Material Usage Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = LaborEffId, BookId = BookId, AccountNumber = "51310", Name = "Labor Efficiency Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OhEffId, BookId = BookId, AccountNumber = "51330", Name = "Overhead Efficiency Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = LaborRateId, BookId = BookId, AccountNumber = "51300", Name = "Labor Rate Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = RawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = WipId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = FgId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_APPLIED", GlAccountId = LaborAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_APPLIED", GlAccountId = OhAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "PRODUCTION_VARIANCE", GlAccountId = ProdVarId },
            new AccountDeterminationRule { BookId = BookId, Key = "MATERIAL_USAGE_VARIANCE", GlAccountId = MatUsageId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_EFFICIENCY_VARIANCE", GlAccountId = LaborEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_EFFICIENCY_VARIANCE", GlAccountId = OhEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_RATE_VARIANCE", GlAccountId = LaborRateId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Builds the job's GL WIP state: material in (Dr WIP[job] / Cr RAW) then standard FG relieved
    /// (Dr FG / Cr WIP[job]). Plus time entries for labor/burden.</summary>
    private static async Task SeedJobAsync(AppDbContext db, decimal materialIn, decimal stdFgRelieved, decimal labor, decimal burden, decimal actualLabor = 0m)
    {
        var engine = Engine(db);
        await engine.PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = EntryDate, Source = JournalSource.Inventory, CurrencyId = UsdId,
            IdempotencyKey = $"mat:{JobId}",
            Lines =
            [
                new PostingLine { AccountKey = "INVENTORY_WIP", Debit = materialIn, JobId = JobId },
                new PostingLine { AccountKey = "INVENTORY_RAW", Credit = materialIn },
            ],
        }, 7);
        await engine.PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = EntryDate, Source = JournalSource.Inventory, CurrencyId = UsdId,
            IdempotencyKey = $"fg:{JobId}",
            Lines =
            [
                new PostingLine { AccountKey = "INVENTORY_FG", Debit = stdFgRelieved },
                new PostingLine { AccountKey = "INVENTORY_WIP", Credit = stdFgRelieved, JobId = JobId },
            ],
        }, 7);

        db.Set<TimeEntry>().Add(new TimeEntry
        {
            JobId = JobId, UserId = 7, Date = EntryDate, DurationMinutes = 60,
            LaborCost = labor, ActualLaborCost = actualLabor, BurdenCost = burden,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> WipByJobAsync(AppDbContext db) =>
        await (from line in db.JournalLines.IgnoreQueryFilters()
               join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
               where line.GlAccountId == WipId && line.JobId == JobId
                  && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
               select line.Debit - line.Credit).SumAsync();

    private static Task<JournalEntry?> EntryByKeyAsync(AppDbContext db, string key) =>
        db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.IdempotencyKey == key);

    [Fact]
    public async Task Close_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        await SeedJobAsync(db, materialIn: 100m, stdFgRelieved: 130m, labor: 25m, burden: 15m);

        var result = await Service(db, fullGlOn: false).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.Posted.Should().BeFalse();
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:WIPABSORB")).Should().BeNull();
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().BeNull();
    }

    [Fact]
    public async Task Close_Unfavorable_AbsorbsLaborOverhead_AndSweepsVariance()
    {
        using var db = await SeedAsync();
        // Material 100 + std FG 130 → WIP −30. Labor 25 + burden 15 = 40 absorbed → WIP +10 → unfavorable 10.
        await SeedJobAsync(db, materialIn: 100m, stdFgRelieved: 130m, labor: 25m, burden: 15m);

        var result = await Service(db, fullGlOn: true).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.LaborAbsorbed.Should().Be(25m);
        result.OverheadAbsorbed.Should().Be(15m);
        result.ProductionVariance.Should().Be(10m); // unfavorable (debit)
        result.Posted.Should().BeTrue();

        var absorb = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:WIPABSORB");
        absorb!.Lines.Single(l => l.GlAccountId == WipId).Debit.Should().Be(40m);
        absorb.Lines.Single(l => l.GlAccountId == LaborAppliedId).Credit.Should().Be(25m);
        absorb.Lines.Single(l => l.GlAccountId == OhAppliedId).Credit.Should().Be(15m);

        var variance = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE");
        variance!.Lines.Single(l => l.GlAccountId == ProdVarId).Debit.Should().Be(10m);
        variance.Lines.Single(l => l.GlAccountId == WipId).Credit.Should().Be(10m);

        (await WipByJobAsync(db)).Should().Be(0m, "the job's WIP is fully cleared after the close");
    }

    [Fact]
    public async Task Close_Favorable_PostsFavorableVariance()
    {
        using var db = await SeedAsync();
        // Material 100 + std FG 130 → WIP −30. Labor 10 + burden 5 = 15 absorbed → WIP −15 → favorable 15.
        await SeedJobAsync(db, materialIn: 100m, stdFgRelieved: 130m, labor: 10m, burden: 5m);

        var result = await Service(db, fullGlOn: true).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.ProductionVariance.Should().Be(-15m); // favorable (credit)

        var variance = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE");
        variance!.Lines.Single(l => l.GlAccountId == WipId).Debit.Should().Be(15m);
        variance.Lines.Single(l => l.GlAccountId == ProdVarId).Credit.Should().Be(15m);

        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_PerfectAbsorption_NoVarianceEntry()
    {
        using var db = await SeedAsync();
        // Material 100 + std FG 130 → WIP −30. Labor 20 + burden 10 = 30 absorbed → WIP 0 → no variance.
        await SeedJobAsync(db, materialIn: 100m, stdFgRelieved: 130m, labor: 20m, burden: 10m);

        var result = await Service(db, fullGlOn: true).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.ProductionVariance.Should().Be(0m);
        result.Posted.Should().BeTrue("labor + overhead were absorbed even though the variance is nil");
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:WIPABSORB")).Should().NotBeNull();
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().BeNull();
        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_WithResolver_DecomposesIntoNamedVariances()
    {
        using var db = await SeedAsync();
        // GL WIP: material 110 in, std FG 130 relieved; labor 25 + burden 12 absorbed at close → residual 17.
        await SeedJobAsync(db, materialIn: 110m, stdFgRelieved: 130m, labor: 25m, burden: 12m);

        // Part std elements: 13/unit = material 10 + labor 2 + overhead 1 (routing); 10 good units produced.
        var part = new Part { PartNumber = "P-STD", Name = "x", ManualCostOverride = 13m };
        db.Add(part);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", EstimatedLaborCost = 2m, EstimatedBurdenCost = 1m });
        db.Add(new MaterialIssue { JobId = JobId, PartId = part.Id, Quantity = 11m, UnitCost = 10m, IssuedById = 7, IssuedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), IssueType = MaterialIssueType.Issue });
        db.Add(new ProductionRun { JobId = JobId, PartId = part.Id, RunNumber = "RUN-STD", TargetQuantity = 10, CompletedQuantity = 10, ReceivedQuantity = 10, ReceivedToStockAt = DateTimeOffset.UtcNow, Status = ProductionRunStatus.Completed });
        await db.SaveChangesAsync();

        var result = await ServiceWithStd(db).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.MaterialUsageVariance.Should().Be(10m);      // 110 actual − 100 std
        result.LaborEfficiencyVariance.Should().Be(5m);     // 25 − 20
        result.OverheadEfficiencyVariance.Should().Be(2m);  // 12 − 10
        result.ProductionVarianceResidual.Should().Be(0m);  // 17 − (10+5+2)
        result.ProductionVariance.Should().Be(17m);         // total residual cleared

        var variance = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE");
        variance!.Lines.Single(l => l.GlAccountId == MatUsageId).Debit.Should().Be(10m);
        variance.Lines.Single(l => l.GlAccountId == LaborEffId).Debit.Should().Be(5m);
        variance.Lines.Single(l => l.GlAccountId == OhEffId).Debit.Should().Be(2m);
        variance.Lines.Should().NotContain(l => l.GlAccountId == ProdVarId, "the named variances explain the residual fully");
        variance.Lines.Single(l => l.GlAccountId == WipId).Credit.Should().Be(17m);

        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_WithResolver_SplitsLaborIntoRateAndEfficiency()
    {
        using var db = await SeedAsync();
        // Labor paid above standard: std-rate cost 25, actual-rate cost 28 → rate variance 3. Absorbed at actual.
        // WIP: material 110 + (28 labor + 12 burden) − std FG 130 = 20 residual.
        await SeedJobAsync(db, materialIn: 110m, stdFgRelieved: 130m, labor: 25m, burden: 12m, actualLabor: 28m);

        var part = new Part { PartNumber = "P-STD2", Name = "x", ManualCostOverride = 13m };
        db.Add(part);
        await db.SaveChangesAsync();
        db.Add(new Operation { PartId = part.Id, StepNumber = 1, Title = "Op", EstimatedLaborCost = 2m, EstimatedBurdenCost = 1m });
        db.Add(new MaterialIssue { JobId = JobId, PartId = part.Id, Quantity = 11m, UnitCost = 10m, IssuedById = 7, IssuedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), IssueType = MaterialIssueType.Issue });
        db.Add(new ProductionRun { JobId = JobId, PartId = part.Id, RunNumber = "RUN-STD2", TargetQuantity = 10, CompletedQuantity = 10, ReceivedQuantity = 10, ReceivedToStockAt = DateTimeOffset.UtcNow, Status = ProductionRunStatus.Completed });
        await db.SaveChangesAsync();

        var result = await ServiceWithStd(db).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        result.MaterialUsageVariance.Should().Be(10m);      // 110 − 100
        result.LaborRateVariance.Should().Be(3m);           // 28 actual − 25 standard
        result.LaborEfficiencyVariance.Should().Be(5m);     // 25 std-rate − 20 standard
        result.OverheadEfficiencyVariance.Should().Be(2m);  // 12 − 10
        result.ProductionVarianceResidual.Should().Be(0m);  // 20 − (10+3+5+2)

        var variance = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE");
        variance!.Lines.Single(l => l.GlAccountId == LaborRateId).Debit.Should().Be(3m);
        variance.Lines.Single(l => l.GlAccountId == LaborEffId).Debit.Should().Be(5m);
        variance.Lines.Single(l => l.GlAccountId == WipId).Credit.Should().Be(20m);

        // Labor absorbed into WIP at the ACTUAL rate (28), not the standard (25).
        var absorb = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:WIPABSORB");
        absorb!.Lines.Single(l => l.GlAccountId == LaborAppliedId).Credit.Should().Be(28m);

        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        await SeedJobAsync(db, materialIn: 100m, stdFgRelieved: 130m, labor: 25m, burden: 15m);
        var service = Service(db, fullGlOn: true);

        await service.CloseJobProductionCostAsync(JobId, EntryDate, 7);
        await service.CloseJobProductionCostAsync(JobId, EntryDate, 7);

        (await db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(e => e.IdempotencyKey == $"Inventory:Job:{JobId}:WIPABSORB")).Should().Be(1);
        (await db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(e => e.IdempotencyKey == $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().Be(1);
        (await WipByJobAsync(db)).Should().Be(0m);
    }
}
