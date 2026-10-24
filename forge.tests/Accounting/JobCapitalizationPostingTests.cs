using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.Sod;
using Forge.Api.Features.Jobs;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Capitalizing a job as a company asset under CAP-ACCT-FULLGL. Proves the job's conversion cost is absorbed into
/// WIP once, its whole WIP balance moves to a fixed-asset account (explicit, else FIXED_ASSETS of an asset type),
/// a later job-cost close neither re-absorbs nor sweeps it to variance, nothing posts for a zero-cost job or with
/// the GL dark, and the dispose handler records the capitalized amount as the asset's acquisition cost. Also proves
/// that disposing a job to inventory or scrap runs the job-cost close, so its WIP residual goes to production
/// variance once.
/// </summary>
public class JobCapitalizationPostingTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int RawId = 130;
    private const int WipId = 131;
    private const int LaborAppliedId = 140;
    private const int OhAppliedId = 141;
    private const int ProdVarId = 142;
    private const int FixedAssetsId = 150;
    private const int ToolingId = 151;
    private const int ExpenseId = 600;

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

    private static ProductionVariancePostingService Service(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), new JobCostService(db));

    private static ProductionVariancePostingService ServiceForRole(AppDbContext db, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, role)], "Test"));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock(),
            new GlBoundaryAuthorizer(new CurrentUserCapabilities(accessor.Object), NullLogger<GlBoundaryAuthorizer>.Instance));
        return new(db, engine, new FakeCapabilities(true), new JobCostService(db));
    }

    private static async Task<AppDbContext> SeedAsync(int? fixedAssetsKeyAccountId = FixedAssetsId)
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
            new GlAccount { Id = LaborAppliedId, BookId = BookId, AccountNumber = "51210", Name = "Labor Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = OhAppliedId, BookId = BookId, AccountNumber = "51220", Name = "Overhead Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ProdVarId, BookId = BookId, AccountNumber = "51200", Name = "Production Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FixedAssetsId, BookId = BookId, AccountNumber = "15000", Name = "Machinery & Equipment", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ToolingId, BookId = BookId, AccountNumber = "15100", Name = "Tooling", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = RawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = WipId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_APPLIED", GlAccountId = LaborAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_APPLIED", GlAccountId = OhAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "PRODUCTION_VARIANCE", GlAccountId = ProdVarId });
        if (fixedAssetsKeyAccountId is int keyAccount)
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = ProductionVariancePostingService.KeyFixedAssets, GlAccountId = keyAccount });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task SeedJobCostAsync(AppDbContext db, decimal materialIn, decimal labor)
    {
        if (materialIn > 0m)
        {
            await Engine(db).PostAsync(new PostingRequest
            {
                BookId = BookId, EntryDate = EntryDate, Source = JournalSource.Inventory, CurrencyId = UsdId,
                IdempotencyKey = $"mat:{JobId}",
                Lines =
                [
                    new PostingLine { AccountKey = "INVENTORY_WIP", Debit = materialIn, JobId = JobId },
                    new PostingLine { AccountKey = "INVENTORY_RAW", Credit = materialIn },
                ],
            }, 7);
        }

        db.Set<TimeEntry>().Add(new TimeEntry { JobId = JobId, UserId = 7, Date = EntryDate, DurationMinutes = 240, LaborCost = labor });
        await db.SaveChangesAsync();
    }

    private static async Task<decimal> WipByJobAsync(AppDbContext db) =>
        await (from line in db.JournalLines.IgnoreQueryFilters()
               join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
               where line.GlAccountId == WipId && line.JobId == JobId
               select line.Debit - line.Credit).SumAsync();

    private static Task<JournalEntry?> EntryByKeyAsync(AppDbContext db, string key) =>
        db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).FirstOrDefaultAsync(e => e.IdempotencyKey == key);

    [Fact]
    public async Task Capitalize_FullGlOn_MovesMaterialAndLaborFromWipToFixedAssets()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 120m, labor: 80m);

        var result = await Service(db).CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        result.Posted.Should().BeTrue();
        result.LaborAbsorbed.Should().Be(80m);
        result.Capitalized.Should().Be(200m);
        result.AssetGlAccountId.Should().Be(FixedAssetsId);

        var capitalize = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:CAPITALIZE");
        capitalize!.Lines.Single(l => l.GlAccountId == FixedAssetsId).Debit.Should().Be(200m);
        var wipLine = capitalize.Lines.Single(l => l.GlAccountId == WipId);
        wipLine.Credit.Should().Be(200m);
        wipLine.JobId.Should().Be(JobId);
        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task Capitalize_ByAShopManagerWithoutGlPosting_PostsOnTheSubledgerPath()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 120m, labor: 80m);

        var result = await ServiceForRole(db, "Manager").CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        result.Capitalized.Should().Be(200m);
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:CAPITALIZE"))!.PostedBy.Should().Be(7);
    }

    [Fact]
    public async Task Close_ByAShopManagerWithoutGlPosting_IsStillDenied()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 120m, labor: 80m);

        var act = () => ServiceForRole(db, "Manager").CloseJobProductionCostAsync(JobId, EntryDate, 7);

        await act.Should().ThrowAsync<GlAuthorizationException>();
    }

    [Fact]
    public async Task Capitalize_ExplicitAssetAccount_BeatsTheKey()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 50m, labor: 0m);

        var result = await Service(db).CapitalizeJobCostAsync(JobId, ToolingId, EntryDate, 7);

        result.AssetGlAccountId.Should().Be(ToolingId);
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:CAPITALIZE"))!.Lines
            .Single(l => l.GlAccountId == ToolingId).Debit.Should().Be(50m);
    }

    [Fact]
    public async Task Capitalize_ExplicitExpenseAccount_IsRejected()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 50m, labor: 0m);

        var act = () => Service(db).CapitalizeJobCostAsync(JobId, ExpenseId, EntryDate, 7);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Capitalize_KeyOnANonAssetAccount_IsTreatedAsMissing()
    {
        using var db = await SeedAsync(fixedAssetsKeyAccountId: ExpenseId);
        await SeedJobCostAsync(db, materialIn: 50m, labor: 0m);

        var act = () => Service(db).CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*fixed-asset GL account*");
    }

    [Fact]
    public async Task Capitalize_WithWipAndNoAssetAccount_IsRejected()
    {
        using var db = await SeedAsync(fixedAssetsKeyAccountId: null);
        await SeedJobCostAsync(db, materialIn: 50m, labor: 0m);

        var act = () => Service(db).CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Capitalize_ZeroCostJob_PostsNothing()
    {
        using var db = await SeedAsync(fixedAssetsKeyAccountId: null);
        await SeedJobCostAsync(db, materialIn: 0m, labor: 0m);

        var result = await Service(db).CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        result.Posted.Should().BeFalse();
        result.Capitalized.Should().Be(0m);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capitalize_FullGlOff_PostsNothing()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 0m, labor: 80m);

        var result = await Service(db, fullGlOn: false).CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        result.Posted.Should().BeFalse();
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capitalize_ThenCloseJobProductionCost_DoesNotReabsorbOrSweep()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 120m, labor: 80m);
        var svc = Service(db);
        await svc.CapitalizeJobCostAsync(JobId, null, EntryDate, 7);

        var close = await svc.CloseJobProductionCostAsync(JobId, EntryDate, 7);

        close.ProductionVariance.Should().Be(0m);
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().BeNull();
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.IdempotencyKey == $"Inventory:Job:{JobId}:WIPABSORB")).Should().Be(1);
        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task DisposeJob_CapitalizeAsAsset_RecordsTheCapitalizedCostOnTheAsset()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 120m, labor: 80m);

        var job = new Job { Id = JobId, JobNumber = "J-42", Title = "Drill fixture", TrackTypeId = 1, CurrentStageId = 1 };
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.FindAsync(JobId, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero));
        var capabilities = new FakeCapabilities(true);

        var handler = new DisposeJobHandler(
            jobRepo.Object, new AssetRepository(db), mediator.Object, hub.Object, clock.Object, capabilities, db,
            Service(db));

        await handler.Handle(
            new DisposeJobCommand(JobId, new DisposeJobRequestModel(JobDisposition.CapitalizeAsAsset, "Fixture"), 7),
            CancellationToken.None);

        var asset = await db.Assets.SingleAsync(a => a.SourceJobId == JobId);
        asset.AcquisitionCost.Should().Be(200m);
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:CAPITALIZE")).Should().NotBeNull();
        job.ActivityLogs.Should().Contain(l => l.Description.Contains("200.00"));
    }

    private static async Task RelieveWipAtStandardAsync(AppDbContext db, decimal standard) =>
        await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = EntryDate, Source = JournalSource.Inventory, CurrencyId = UsdId,
            IdempotencyKey = $"fg:{JobId}",
            Lines =
            [
                new PostingLine { AccountKey = "INVENTORY_RAW", Debit = standard },
                new PostingLine { AccountKey = "INVENTORY_WIP", Credit = standard, JobId = JobId },
            ],
        }, 7);

    private static DisposeJobHandler DisposeHandler(AppDbContext db, Job job, ProductionVariancePostingService service)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.FindAsync(job.Id, It.IsAny<CancellationToken>())).ReturnsAsync(job);
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetJobByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobDetailResponseModel)null!);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<BoardHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero));

        return new DisposeJobHandler(
            jobRepo.Object, new AssetRepository(db), mediator.Object, hub.Object, clock.Object,
            new FakeCapabilities(true), db, service);
    }

    [Theory]
    [InlineData(JobDisposition.AddToInventory)]
    [InlineData(JobDisposition.Scrap)]
    public async Task DisposeJob_AfterStandardReliefAboveActual_SweepsTheWipResidualToVarianceOnce(JobDisposition disposition)
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 100m, labor: 0m);
        await RelieveWipAtStandardAsync(db, 300m);
        (await WipByJobAsync(db)).Should().Be(-200m);
        var job = new Job { Id = JobId, JobNumber = "J-42", Title = "Bracket", TrackTypeId = 1, CurrentStageId = 1, PartId = 9 };

        await DisposeHandler(db, job, ServiceForRole(db, "Manager")).Handle(
            new DisposeJobCommand(JobId, new DisposeJobRequestModel(disposition, "done"), 7),
            CancellationToken.None);

        (await WipByJobAsync(db)).Should().Be(0m);
        var variance = await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE");
        variance!.Lines.Single(l => l.GlAccountId == ProdVarId).Credit.Should().Be(200m);
        job.ActivityLogs.Should().Contain(l => l.Description.Contains("200.00"));

        var again = await Service(db).CloseJobProductionCostAsync(JobId, EntryDate, 7);

        again.Posted.Should().BeFalse();
        (await db.JournalEntries.IgnoreQueryFilters()
            .CountAsync(e => e.IdempotencyKey == $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().Be(1);
        (await WipByJobAsync(db)).Should().Be(0m);
    }

    [Fact]
    public async Task DisposeJob_HoldForReview_LeavesWipForTheLaterDisposition()
    {
        using var db = await SeedAsync();
        await SeedJobCostAsync(db, materialIn: 100m, labor: 0m);
        await RelieveWipAtStandardAsync(db, 300m);
        var job = new Job { Id = JobId, JobNumber = "J-42", Title = "Bracket", TrackTypeId = 1, CurrentStageId = 1, PartId = 9 };

        await DisposeHandler(db, job, Service(db)).Handle(
            new DisposeJobCommand(JobId, new DisposeJobRequestModel(JobDisposition.HoldForReview, "check"), 7),
            CancellationToken.None);

        (await WipByJobAsync(db)).Should().Be(-200m);
        (await EntryByKeyAsync(db, $"Inventory:Job:{JobId}:PRODVARIANCE")).Should().BeNull();
    }
}
