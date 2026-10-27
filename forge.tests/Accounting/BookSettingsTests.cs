using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Book settings: the maker-checker threshold (null = off, never negative) and the reporting time zone,
/// which is refused with <c>book-timezone-locked</c> once the book has a posted entry; and the
/// soft-closed-period override reason that lands in the book's activity log.
/// </summary>
public class BookSettingsTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int PeriodId = 1000;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static IHttpContextAccessor HttpAccessorFor(int userId) => new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
        },
    };

    private static async Task<AppDbContext> SeedAsync(FiscalPeriodStatus periodStatus = FiscalPeriodStatus.Open)
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
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = PeriodId, FiscalYearId = 10, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = periodStatus,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateManualJournalEntryCommand Entry(decimal amount, bool overrideSoftClose = false, string? reason = null) => new(
        BookId, new DateOnly(2026, 1, 15), UsdId, "Manual entry", overrideSoftClose,
        [
            new CreateManualJournalLineModel(CashId, null, null, null, null, null, amount, 0m, null),
            new CreateManualJournalLineModel(RevenueId, null, null, null, null, null, 0m, amount, null),
        ],
        OverrideReason: reason);

    private static Task<ManualJournalEntryResult> PostAsync(AppDbContext db, CreateManualJournalEntryCommand command)
        => new CreateManualJournalEntryHandler(
                new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
                HttpAccessorFor(42),
                db)
            .Handle(command, CancellationToken.None);

    [Fact]
    public async Task Get_ReturnsThresholdZoneAndUnlockedForAnUnpostedBook()
    {
        using var db = await SeedAsync();

        var settings = await new GetBookSettingsHandler(db).Handle(new GetBookSettingsQuery(BookId), CancellationToken.None);

        settings.MakerCheckerThreshold.Should().BeNull();
        settings.ReportingTimeZone.Should().Be("America/New_York");
        settings.TimeZoneLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Update_SetsThresholdAndZone_AndLogsTheChange()
    {
        using var db = await SeedAsync();

        var result = await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, 2500m, "America/Denver"), CancellationToken.None);

        result.MakerCheckerThreshold.Should().Be(2500m);
        result.ReportingTimeZone.Should().Be("America/Denver");
        (await db.ActivityLogs.SingleAsync(l => l.EntityType == "Book")).Action.Should().Be("book-settings-updated");
    }

    [Fact]
    public async Task Update_NullThreshold_TurnsMakerCheckerOff()
    {
        using var db = await SeedAsync();
        (await db.Books.SingleAsync()).MakerCheckerThreshold = 1000m;
        await db.SaveChangesAsync();

        var result = await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, null, null), CancellationToken.None);

        result.MakerCheckerThreshold.Should().BeNull();
        (await PostAsync(db, Entry(5000m))).Status.Should().Be(JournalEntryStatus.Posted.ToString());
    }

    [Fact]
    public async Task Update_ThresholdThenEntryAboveIt_GoesToPendingApproval()
    {
        using var db = await SeedAsync();
        await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, 1000m, null), CancellationToken.None);

        (await PostAsync(db, Entry(1500m))).Status.Should().Be(JournalEntryStatus.PendingApproval.ToString());
        (await PostAsync(db, Entry(400m))).Status.Should().Be(JournalEntryStatus.Posted.ToString());
    }

    [Theory]
    [InlineData(-0.01, "America/New_York", "maker-checker-threshold-negative")]
    [InlineData(10, "Mars/Olympus_Mons", "time-zone-unknown")]
    public void Validator_RefusesNegativeThresholdAndUnknownZone(double threshold, string zone, string code)
    {
        var result = new UpdateBookSettingsValidator()
            .Validate(new UpdateBookSettingsCommand(BookId, (decimal)threshold, zone));

        result.Errors.Select(e => e.ErrorCode).Should().Contain(code);
    }

    [Fact]
    public void Validator_AcceptsZeroThresholdAndKnownZone()
    {
        new UpdateBookSettingsValidator()
            .Validate(new UpdateBookSettingsCommand(BookId, 0m, "Europe/Berlin"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ZoneAfterAPostedEntry_IsRefusedAsLocked()
    {
        using var db = await SeedAsync();
        await PostAsync(db, Entry(100m));

        var act = () => new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, null, "America/Chicago"), CancellationToken.None);

        (await act.Should().ThrowAsync<CodedConflictException>()).Which.Code.Should().Be(BookTimeZoneRules.LockedCode);
        (await db.Books.SingleAsync()).ReportingTimeZone.Should().Be("America/New_York");
        (await new GetBookSettingsHandler(db).Handle(new GetBookSettingsQuery(BookId), CancellationToken.None))
            .TimeZoneLocked.Should().BeTrue();
    }

    [Fact]
    public async Task Update_SameZoneAfterPosting_StillSavesTheThreshold()
    {
        using var db = await SeedAsync();
        await PostAsync(db, Entry(100m));

        var result = await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, 750m, "America/New_York"), CancellationToken.None);

        result.MakerCheckerThreshold.Should().Be(750m);
    }

    [Fact]
    public async Task SoftClosedOverride_WithReason_PostsAndRecordsTheReason()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);

        var result = await PostAsync(db, Entry(100m, overrideSoftClose: true, reason: "Late vendor invoice"));

        result.Status.Should().Be(JournalEntryStatus.Posted.ToString());
        var log = await db.ActivityLogs.SingleAsync(l => l.Action == "soft-close-override");
        log.EntityType.Should().Be("Book");
        log.Description.Should().Contain("Late vendor invoice").And.Contain("Jan 2026");
    }

    [Fact]
    public async Task SoftClosedOverride_OnResubmit_RecordsTheNewReason()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        await new UpdateBookSettingsHandler(db)
            .Handle(new UpdateBookSettingsCommand(BookId, 50m, null), CancellationToken.None);
        var pending = await PostAsync(db, Entry(100m, overrideSoftClose: true, reason: "First reason"));
        await new RejectJournalEntryHandler(db)
            .Handle(new RejectJournalEntryCommand(pending.Id, "Wrong account"), CancellationToken.None);

        var resubmitted = await new ResubmitJournalEntryHandler(
                new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
                HttpAccessorFor(42),
                db)
            .Handle(new ResubmitJournalEntryCommand(pending.Id, Entry(100m, overrideSoftClose: true, reason: "Corrected late invoice")),
                CancellationToken.None);

        resubmitted.Status.Should().Be(JournalEntryStatus.PendingApproval.ToString());
        (await db.ActivityLogs.Where(l => l.Action == "soft-close-override").Select(l => l.Description).ToListAsync())
            .Should().HaveCount(2).And.Contain(d => d.Contains("Corrected late invoice"));
    }

    [Fact]
    public async Task SoftClosedPeriod_WithoutOverride_IsRefused()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);

        var act = () => PostAsync(db, Entry(100m));

        await act.Should().ThrowAsync<PostingException>();
    }
}
