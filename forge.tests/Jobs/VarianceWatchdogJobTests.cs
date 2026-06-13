using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Notifications;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

/// <summary>
/// §10.6 variance watchdog: threshold triggering (absolute floor + percent-of-COGS paths), the
/// per-(account, period) dedupe across re-runs, the new-period re-arm, the CAP-ACCT-FULLGL silent
/// no-op, the friendly copy (amount + % of COGS + cost-roll vs rates/routings suggestion), and the
/// Controller-role fan-out. Hangfire is NOT running — the job is instantiated directly with mocks,
/// mirroring the payment-transmission job tests.
/// </summary>
public class VarianceWatchdogJobTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int JanPeriodId = 1000;
    private const int FebPeriodId = 1001;
    private const int PpvAccountId = 201;
    private const int LaborRateAccountId = 202;
    private const int CogsAccountId = 250;

    private static readonly DateTimeOffset JanNow = new(2026, 1, 15, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FebNow = new(2026, 2, 15, 6, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod { Id = JanPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "2026-01", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = FiscalPeriodStatus.Open },
            new FiscalPeriod { Id = FebPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 2, Name = "2026-02", StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = PpvAccountId, BookId = BookId, AccountNumber = "51000", Name = "Purchase Price Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = LaborRateAccountId, BookId = BookId, AccountNumber = "52000", Name = "Labor Rate Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CogsAccountId, BookId = BookId, AccountNumber = "50000", Name = "Cost of Goods Sold", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_PRICE_VARIANCE", GlAccountId = PpvAccountId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_RATE_VARIANCE", GlAccountId = LaborRateAccountId },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = CogsAccountId });
        await db.SaveChangesAsync();
        return db;
    }

    private static long _nextEntryId = 1;

    private static async Task PostAsync(AppDbContext db, int glAccountId, decimal debit, DateOnly entryDate, int periodId)
    {
        var id = Interlocked.Increment(ref _nextEntryId);
        db.JournalEntries.Add(new JournalEntry
        {
            Id = id, BookId = BookId, EntryNumber = id, EntryDate = entryDate,
            FiscalPeriodId = periodId, FiscalYearId = FiscalYearId,
            Source = JournalSource.Manual, CurrencyId = UsdId, Status = JournalEntryStatus.Posted,
            Lines =
            [
                new JournalLine { BookId = BookId, LineNumber = 1, GlAccountId = glAccountId, Debit = debit, CurrencyId = UsdId, TxnAmount = debit, FunctionalAmount = debit, FxRate = 1m },
            ],
        });
        await db.SaveChangesAsync();
    }

    private static Mock<UserManager<ApplicationUser>> ControllersReturning(params ApplicationUser[] users)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager
            .Setup(x => x.GetUsersInRoleAsync(VarianceWatchdogJob.ControllerRoleName))
            .ReturnsAsync(users.ToList());
        return userManager;
    }

    private static (Mock<IMediator> Mediator, List<CreateNotificationRequestModel> Sent) CapturingMediator()
    {
        var sent = new List<CreateNotificationRequestModel>();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationResponseModel>, CancellationToken>(
                (cmd, _) => sent.Add(((CreateNotificationCommand)cmd).Data))
            .ReturnsAsync((NotificationResponseModel)null!);
        return (mediator, sent);
    }

    private static VarianceWatchdogJob BuildJob(
        AppDbContext db,
        Mock<IMediator> mediator,
        Mock<UserManager<ApplicationUser>>? userManager = null,
        bool fullGlOn = true,
        DateTimeOffset? now = null,
        Mock<ISettingsService>? settings = null)
    {
        var capabilities = new Mock<ICapabilitySnapshotProvider>();
        capabilities.Setup(c => c.IsEnabled("CAP-ACCT-FULLGL")).Returns(fullGlOn);

        return new VarianceWatchdogJob(
            db,
            capabilities.Object,
            new VarianceReportService(db),
            (settings ?? new Mock<ISettingsService>()).Object, // loose mock → null values → coded defaults (5% / 500)
            (userManager ?? ControllersReturning(new ApplicationUser { Id = 30 })).Object,
            mediator.Object,
            new FixedClock(now ?? JanNow),
            NullLogger<VarianceWatchdogJob>.Instance);
    }

    // ── Triggering: absolute floor + percent-of-COGS paths ─────────────────────────────────────

    [Fact]
    public async Task Run_VarianceAboveFloor_NoCogs_Notifies()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 600m, new DateOnly(2026, 1, 10), JanPeriodId); // > 500 floor, COGS 0

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle();
        sent[0].Severity.Should().Be("warning");
        sent[0].Title.Should().Be("Variance review suggested");
    }

    [Fact]
    public async Task Run_VarianceAbovePercentOfCogs_Notifies()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CogsAccountId, 20000m, new DateOnly(2026, 1, 5), JanPeriodId); // 5% → 1,000 threshold
        await PostAsync(db, PpvAccountId, 1200m, new DateOnly(2026, 1, 10), JanPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle();
        sent[0].Message.Should().Contain("$1,200.00").And.Contain("6.0% of this period's COGS");
    }

    [Fact]
    public async Task Run_VarianceBelowBothThresholds_DoesNotNotify()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CogsAccountId, 20000m, new DateOnly(2026, 1, 5), JanPeriodId);
        await PostAsync(db, PpvAccountId, 600m, new DateOnly(2026, 1, 10), JanPeriodId); // > 500 floor but < 1,000 (5% of COGS)

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_SettingsOverrideThresholds()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 300m, new DateOnly(2026, 1, 10), JanPeriodId); // under default 500 floor

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetStringAsync(VarianceWatchdogSettings.AbsoluteFloorKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("250");
        settings.Setup(s => s.GetStringAsync(VarianceWatchdogSettings.PercentOfCogsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync("5");

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator, settings: settings).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle(); // 300 > overridden 250 floor
    }

    // ── Dedupe + re-arm ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_Twice_SamePeriod_NotifiesOnlyOnce()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 600m, new DateOnly(2026, 1, 10), JanPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle("re-runs in the same period must not re-notify");
        var state = await db.SystemSettings.SingleAsync(s => s.Key == VarianceWatchdogJob.NotifiedStateKey);
        state.Value.Should().Contain($"{JanPeriodId}:PURCHASE_PRICE_VARIANCE");
    }

    [Fact]
    public async Task Run_NewPeriod_ReArmsAndPrunesOldState()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 600m, new DateOnly(2026, 1, 10), JanPeriodId);
        await PostAsync(db, PpvAccountId, 700m, new DateOnly(2026, 2, 10), FebPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator, now: JanNow).RunAsync(CancellationToken.None);
        await BuildJob(db, mediator, now: FebNow).RunAsync(CancellationToken.None);

        sent.Should().HaveCount(2, "a new fiscal period re-arms every account");
        var state = await db.SystemSettings.SingleAsync(s => s.Key == VarianceWatchdogJob.NotifiedStateKey);
        state.Value.Should().Contain($"{FebPeriodId}:PURCHASE_PRICE_VARIANCE")
            .And.NotContain($"{JanPeriodId}:", "entries for other periods are pruned");
    }

    // ── Gating ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_FullGlOff_IsSilentNoOp()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 9999m, new DateOnly(2026, 1, 10), JanPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator, fullGlOn: false).RunAsync(CancellationToken.None);

        sent.Should().BeEmpty();
        (await db.SystemSettings.AnyAsync(s => s.Key == VarianceWatchdogJob.NotifiedStateKey)).Should().BeFalse();
    }

    // ── Copy ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_MaterialVariance_SuggestsCostRoll_AndNamesAccountWithAmount()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CogsAccountId, 20000m, new DateOnly(2026, 1, 5), JanPeriodId);
        await PostAsync(db, PpvAccountId, 1240m, new DateOnly(2026, 1, 10), JanPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle();
        sent[0].Message.Should().StartWith("Purchase price variance has reached $1,240.00")
            .And.Contain("6.2% of this period's COGS")
            .And.Contain("consider a standard-cost roll");
    }

    [Fact]
    public async Task Run_LaborVariance_SuggestsRatesAndRoutings_NotCostRoll()
    {
        using var db = await SeedAsync();
        await PostAsync(db, LaborRateAccountId, 800m, new DateOnly(2026, 1, 10), JanPeriodId);

        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator).RunAsync(CancellationToken.None);

        sent.Should().ContainSingle();
        sent[0].Message.Should().StartWith("Labor rate variance has reached $800.00")
            .And.Contain("rates and routings")
            .And.NotContain("standard-cost roll");
    }

    // ── Controller-role targeting ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_TwoControllers_EachReceivesTheNotification()
    {
        using var db = await SeedAsync();
        await PostAsync(db, PpvAccountId, 600m, new DateOnly(2026, 1, 10), JanPeriodId);

        var controllers = ControllersReturning(
            new ApplicationUser { Id = 30 }, new ApplicationUser { Id = 31 });
        var (mediator, sent) = CapturingMediator();
        await BuildJob(db, mediator, userManager: controllers).RunAsync(CancellationToken.None);

        sent.Should().HaveCount(2);
        sent.Select(n => n.UserId).Should().BeEquivalentTo([30, 31]);
        sent.Should().OnlyContain(n => n.Source == "variance-watchdog");
    }
}
