using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
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
/// Disposing a job that never reaches stock closes its work in process against real Postgres: shipped to the
/// customer, cost of goods sold takes the standard of the shipped quantity and the difference goes to the named
/// variances (all of it to cost of goods sold with no standard); disposed as Other, the WIP is written off to the
/// chosen expense account or production variance. Either way the job's WIP ends at zero and a later close posts
/// nothing more.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DisposedJobCostClosePostgresTests(PostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
    }

    private const int BookId = 1;
    private const int UsdId = 1;
    private const int RawId = 130;
    private const int WipId = 131;
    private const int LaborAppliedId = 140;
    private const int OhAppliedId = 141;
    private const int ProdVarId = 142;
    private const int MatUsageId = 143;
    private const int LaborRateId = 144;
    private const int LaborEffId = 145;
    private const int OhEffId = 146;
    private const int CogsId = 500;
    private const int ScrapExpenseId = 610;
    private const int FixedAssetId = 150;

    private static readonly DateOnly EntryDate = new(2026, 1, 20);

    private sealed record Seeded(int UserId, int JobId, int PartId);

    private sealed class FixedStandard(StandardCostElements unit) : IStandardCostResolver
    {
        public Task<StandardCostElements> ResolveAsync(int partId, CancellationToken ct = default) => Task.FromResult(unit);
    }

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    private static ProductionVariancePostingService Service(AppDbContext db, IStandardCostResolver? standard)
        => new(db, Engine(db), new StubCapabilitySnapshotProvider("CAP-ACCT-FULLGL"), new JobCostService(db), standardCost: standard);

    private async Task<Seeded> SeedAsync()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "UTC", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        GlAccount Account(int id, string number, string name, AccountType type, NormalBalance balance, bool inventory = false) => new()
        {
            Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = type, NormalBalance = balance,
            IsControlAccount = inventory, ControlType = inventory ? ControlAccountType.Inventory : null, IsPostable = true, IsActive = true,
        };
        db.Set<GlAccount>().AddRange(
            Account(RawId, "13100", "Inventory — Raw", AccountType.Asset, NormalBalance.Debit, inventory: true),
            Account(WipId, "13200", "Inventory — WIP", AccountType.Asset, NormalBalance.Debit, inventory: true),
            Account(FixedAssetId, "15000", "Machinery", AccountType.Asset, NormalBalance.Debit),
            Account(LaborAppliedId, "51210", "Labor Absorbed", AccountType.Expense, NormalBalance.Credit),
            Account(OhAppliedId, "51220", "Overhead Absorbed", AccountType.Expense, NormalBalance.Credit),
            Account(ProdVarId, "51200", "Production Variance", AccountType.Expense, NormalBalance.Debit),
            Account(MatUsageId, "51300", "Material Usage Variance", AccountType.Expense, NormalBalance.Debit),
            Account(LaborRateId, "51310", "Labor Rate Variance", AccountType.Expense, NormalBalance.Debit),
            Account(LaborEffId, "51320", "Labor Efficiency Variance", AccountType.Expense, NormalBalance.Debit),
            Account(OhEffId, "51330", "Overhead Efficiency Variance", AccountType.Expense, NormalBalance.Debit),
            Account(CogsId, "50000", "Cost of Goods Sold", AccountType.Expense, NormalBalance.Debit),
            Account(ScrapExpenseId, "61000", "Scrap and Rework", AccountType.Expense, NormalBalance.Debit));
        await db.SaveChangesAsync();
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = RawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = WipId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_APPLIED", GlAccountId = LaborAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_APPLIED", GlAccountId = OhAppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "PRODUCTION_VARIANCE", GlAccountId = ProdVarId },
            new AccountDeterminationRule { BookId = BookId, Key = "MATERIAL_USAGE_VARIANCE", GlAccountId = MatUsageId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_RATE_VARIANCE", GlAccountId = LaborRateId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_EFFICIENCY_VARIANCE", GlAccountId = LaborEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_EFFICIENCY_VARIANCE", GlAccountId = OhEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = CogsId });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new ApplicationUser
        {
            UserName = $"dj{suffix}@forge.local", Email = $"dj{suffix}@forge.local", FirstName = "Dispose", LastName = "Job", IsActive = true,
        };
        var track = new TrackType { Name = $"Dispose {suffix}", Code = $"dj-{suffix}", IsActive = true };
        var part = new Part { PartNumber = $"DJ-{suffix}", Name = "Bracket", InventoryClass = InventoryClass.FinishedGood };
        var resin = new Part { PartNumber = $"DJR-{suffix}", Name = "Resin", InventoryClass = InventoryClass.Raw };
        db.AddRange(user, track, part, resin);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "Build", Code = "build", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        var job = new Job { JobNumber = $"J-{suffix}", Title = "Brackets", TrackTypeId = track.Id, CurrentStageId = stage.Id, PartId = part.Id };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        db.JobParts.Add(new JobPart { JobId = job.Id, PartId = part.Id, Quantity = 10 });
        db.MaterialIssues.Add(new MaterialIssue
        {
            JobId = job.Id, PartId = resin.Id, Quantity = 12m, UnitCost = 10m, IssuedById = user.Id,
            IssuedAt = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero), IssueType = MaterialIssueType.Issue,
        });
        db.TimeEntries.Add(new TimeEntry { JobId = job.Id, UserId = user.Id, Date = new DateOnly(2026, 1, 12), DurationMinutes = 240, LaborCost = 80m });
        await db.SaveChangesAsync();

        await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 10), Source = JournalSource.Inventory, CurrencyId = UsdId,
            IdempotencyKey = $"mat:{job.Id}",
            Lines =
            [
                new PostingLine { AccountKey = "INVENTORY_WIP", Debit = 120m, JobId = job.Id },
                new PostingLine { AccountKey = "INVENTORY_RAW", Credit = 120m },
            ],
        }, user.Id);

        return new Seeded(user.Id, job.Id, part.Id);
    }

    private static DisposeJobHandler Handler(AppDbContext db, ProductionVariancePostingService service)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(r => r.FindAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int id, CancellationToken ct) => db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct));
        jobRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => db.SaveChangesAsync(ct));
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
            new StubCapabilitySnapshotProvider("CAP-ACCT-FULLGL"), db, service);
    }

    private static async Task<decimal> WipByJobAsync(AppDbContext db, int jobId) =>
        await (from line in db.JournalLines.IgnoreQueryFilters()
               join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
               where line.GlAccountId == WipId && line.JobId == jobId
               select line.Debit - line.Credit).SumAsync();

    private static async Task<Dictionary<int, decimal>> CloseLinesAsync(AppDbContext db, int jobId)
    {
        var key = $"Inventory:Job:{jobId}:PRODVARIANCE";
        return await (from line in db.JournalLines.IgnoreQueryFilters()
                      join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
                      where je.IdempotencyKey == key && line.GlAccountId != WipId
                      group line by line.GlAccountId into g
                      select new { g.Key, Net = g.Sum(l => l.Debit - l.Credit) })
            .ToDictionaryAsync(x => x.Key, x => x.Net);
    }

    private static DisposeJobCommand Dispose(int jobId, JobDisposition disposition, int userId, decimal? goodQuantity = null, int? expenseAccountId = null)
        => new(jobId, new DisposeJobRequestModel(disposition, "Closing out", goodQuantity, ExpenseGlAccountId: expenseAccountId), userId);

    [Fact]
    public async Task ShipToCustomer_closes_wip_to_cogs_at_standard_with_named_variances()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();
        var standard = new FixedStandard(new StandardCostElements(10m, 6m, 2m));

        await Handler(db, Service(db, standard)).Handle(Dispose(s.JobId, JobDisposition.ShipToCustomer, s.UserId, 10m), CancellationToken.None);

        await using var read = fixture.CreateContext();
        (await WipByJobAsync(read, s.JobId)).Should().Be(0m);
        var lines = await CloseLinesAsync(read, s.JobId);
        lines[CogsId].Should().Be(180m);
        lines[MatUsageId].Should().Be(20m);
        lines[LaborEffId].Should().Be(20m);
        lines[OhEffId].Should().Be(-20m);
        lines.Should().NotContainKey(ProdVarId);
        (await read.Jobs.SingleAsync(j => j.Id == s.JobId)).Disposition.Should().Be(JobDisposition.ShipToCustomer);
        (await read.JobActivityLogs.AnyAsync(l => l.JobId == s.JobId && l.Description.Contains("cost of goods sold"))).Should().BeTrue();

        var again = await Service(read, standard).CloseJobProductionCostAsync(s.JobId, EntryDate, s.UserId);
        again.Posted.Should().BeFalse();
        (await WipByJobAsync(read, s.JobId)).Should().Be(0m);
    }

    [Fact]
    public async Task ShipToCustomer_with_no_standard_sends_the_whole_actual_cost_to_cogs()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();

        var close = await Service(db, standard: null).CloseDisposedJobProductionCostAsync(
            s.JobId, EntryDate, s.UserId, new DisposedJobCostTarget(JobDisposition.ShipToCustomer, s.PartId, 10m, null));

        close.CostOfGoodsSold.Should().Be(200m);
        await using var read = fixture.CreateContext();
        (await WipByJobAsync(read, s.JobId)).Should().Be(0m);
        (await CloseLinesAsync(read, s.JobId)).Should().Equal(new Dictionary<int, decimal> { [CogsId] = 200m });
    }

    [Fact]
    public async Task Other_writes_the_wip_off_to_the_chosen_expense_account()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();

        await Handler(db, Service(db, standard: null)).Handle(
            Dispose(s.JobId, JobDisposition.Other, s.UserId, expenseAccountId: ScrapExpenseId), CancellationToken.None);

        await using var read = fixture.CreateContext();
        (await WipByJobAsync(read, s.JobId)).Should().Be(0m);
        (await CloseLinesAsync(read, s.JobId)).Should().Equal(new Dictionary<int, decimal> { [ScrapExpenseId] = 200m });
    }

    [Fact]
    public async Task Other_without_an_account_writes_the_wip_off_to_production_variance()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();

        await Handler(db, Service(db, standard: null)).Handle(Dispose(s.JobId, JobDisposition.Other, s.UserId), CancellationToken.None);

        await using var read = fixture.CreateContext();
        (await WipByJobAsync(read, s.JobId)).Should().Be(0m);
        (await CloseLinesAsync(read, s.JobId)).Should().Equal(new Dictionary<int, decimal> { [ProdVarId] = 200m });
    }

    [Fact]
    public async Task Other_to_a_non_expense_account_is_refused_and_nothing_changes()
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();

        var act = () => Handler(db, Service(db, standard: null)).Handle(
            Dispose(s.JobId, JobDisposition.Other, s.UserId, expenseAccountId: FixedAssetId), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        await using var read = fixture.CreateContext();
        (await read.Jobs.SingleAsync(j => j.Id == s.JobId)).Disposition.Should().BeNull();
        (await WipByJobAsync(read, s.JobId)).Should().Be(120m);
    }

    [Theory]
    [InlineData(JobDisposition.HoldForReview)]
    [InlineData(JobDisposition.EnteredInError)]
    public async Task Hold_and_entered_in_error_leave_the_wip_alone(JobDisposition disposition)
    {
        var s = await SeedAsync();
        await using var db = fixture.CreateContext();
        if (disposition == JobDisposition.EnteredInError)
        {
            await db.MaterialIssues.Where(m => m.JobId == s.JobId).ExecuteDeleteAsync();
        }

        await Handler(db, Service(db, standard: null)).Handle(Dispose(s.JobId, disposition, s.UserId), CancellationToken.None);

        await using var read = fixture.CreateContext();
        (await WipByJobAsync(read, s.JobId)).Should().Be(120m);
        (await CloseLinesAsync(read, s.JobId)).Should().BeEmpty();
    }
}
