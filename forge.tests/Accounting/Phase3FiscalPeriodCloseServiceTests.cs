using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
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
/// Phase-3 — fiscal-period close/reopen. Proves the status transitions (and their illegal-transition guards)
/// and that closing actually changes posting behavior via the engine: a soft-closed period blocks a post
/// without an override; a hard-closed period rejects outright.
/// </summary>
public class Phase3FiscalPeriodCloseServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int RevId = 101;

    private static readonly DateOnly EntryDate = new(2026, 3, 15);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static FiscalPeriodCloseService Service(AppDbContext db) => new(db, new SystemClock(), Engine(db));

    private static async Task<AppDbContext> SeedAsync(FiscalPeriodStatus status = FiscalPeriodStatus.Open)
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
            Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = status,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevId });

        await db.SaveChangesAsync();
        return db;
    }

    private static PostingRequest SimpleEntry(bool allowOverride = false) => new()
    {
        BookId = BookId, EntryDate = EntryDate, Source = JournalSource.Manual, CurrencyId = UsdId,
        IdempotencyKey = $"test:{EntryDate}", AllowSoftClosedOverride = allowOverride,
        Lines =
        [
            new PostingLine { AccountKey = "CASH", Debit = 100m, Description = "dr" },
            new PostingLine { AccountKey = "SALES_REVENUE", Credit = 100m, Description = "cr" },
        ],
    };

    [Fact]
    public async Task SoftClose_OpenToSoftClosed()
    {
        using var db = await SeedAsync();
        var result = await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);
        result.Status.Should().Be(FiscalPeriodStatus.SoftClosed);
        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.SoftClosed);
    }

    [Fact]
    public async Task HardClose_FromSoftClosed()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed, actorUserId: 7)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task HardClose_DirectFromOpen()
    {
        using var db = await SeedAsync();
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed, actorUserId: 7)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task Reopen_SoftClosedToOpen()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open, actorUserId: 7)).Status.Should().Be(FiscalPeriodStatus.Open);
    }

    [Fact]
    public async Task HardClosed_IsTerminal_ReopenThrows()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.HardClosed);
        var act = async () => await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open, actorUserId: 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*from HardClosed*");
    }

    [Fact]
    public async Task SameStatus_Throws()
    {
        using var db = await SeedAsync();
        var act = async () => await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open, actorUserId: 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already Open*");
    }

    [Fact]
    public async Task SoftClose_ThenPostWithoutOverride_Throws()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);

        var act = async () => await Engine(db).PostAsync(SimpleEntry(), postedByUserId: 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_SOFT_CLOSED");
    }

    [Fact]
    public async Task SoftClose_ThenPostWithOverride_Succeeds()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);

        var entry = await Engine(db).PostAsync(SimpleEntry(allowOverride: true), postedByUserId: 7);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task HardClose_ThenPost_Throws()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed, actorUserId: 7);

        var act = async () => await Engine(db).PostAsync(SimpleEntry(allowOverride: true), postedByUserId: 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_HARD_CLOSED");
    }

    [Fact]
    public async Task FiscalCalendar_ReturnsYearWithPeriods()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);

        var calendar = await new GetFiscalCalendarHandler(db)
            .Handle(new GetFiscalCalendarQuery(BookId), CancellationToken.None);

        var year = calendar.Should().ContainSingle().Subject;
        year.Id.Should().Be(FiscalYearId);
        var period = year.Periods.Should().ContainSingle().Subject;
        period.Id.Should().Be(PeriodId);
        period.Status.Should().Be(FiscalPeriodStatus.SoftClosed);
    }

    [Fact]
    public async Task Close_StampsCloseAudit()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 42);

        var p = await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId);
        p.ClosedByUserId.Should().Be(42);
        p.ClosedAt.Should().NotBeNull();
        p.ReopenedAt.Should().BeNull();
    }

    [Fact]
    public async Task Reopen_StampsReopenAudit()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open, actorUserId: 99);

        var p = await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId);
        p.ReopenedByUserId.Should().Be(99);
        p.ReopenedAt.Should().NotBeNull();
    }

    // ─────────────────── auto-reversing accruals (§12) ───────────────────

    private const int JanId = 2000;
    private const int FebId = 2001;

    private static async Task<AppDbContext> SeedTwoPeriodsAsync()
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
        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod { Id = JanId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = FiscalPeriodStatus.Open },
            new FiscalPeriod { Id = FebId, FiscalYearId = FiscalYearId, PeriodNumber = 2, Name = "Feb 2026", StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevId });
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<JournalEntry> PostAccrualAsync(AppDbContext db, DateOnly date) => Engine(db).PostAsync(new PostingRequest
    {
        BookId = BookId, EntryDate = date, Source = JournalSource.Manual, CurrencyId = UsdId,
        IdempotencyKey = $"accrual:{date}", AutoReverseNextPeriod = true,
        Lines =
        [
            new PostingLine { AccountKey = "CASH", Debit = 500m, Description = "accrual dr" },
            new PostingLine { AccountKey = "SALES_REVENUE", Credit = 500m, Description = "accrual cr" },
        ],
    }, 7);

    [Fact]
    public async Task Close_ReversesAutoReverseAccruals_IntoNextPeriod()
    {
        using var db = await SeedTwoPeriodsAsync();
        var accrual = await PostAccrualAsync(db, new DateOnly(2026, 1, 15));

        await Service(db).TransitionAsync(JanId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);

        // Original is reversed; the reversal posts on Feb 1 (the day after Jan ends).
        var original = await db.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.Id == accrual.Id);
        original.Status.Should().Be(JournalEntryStatus.Reversed);
        original.ReversedByEntryId.Should().NotBeNull();

        var reversal = await db.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.ReversalOfEntryId == accrual.Id);
        reversal.EntryDate.Should().Be(new DateOnly(2026, 2, 1));
        reversal.FiscalPeriodId.Should().Be(FebId);
    }

    [Fact]
    public async Task Close_AccrualReversal_Idempotent_OnReClose()
    {
        using var db = await SeedTwoPeriodsAsync();
        var accrual = await PostAccrualAsync(db, new DateOnly(2026, 1, 15));
        var svc = Service(db);

        await svc.TransitionAsync(JanId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);
        await svc.TransitionAsync(JanId, FiscalPeriodStatus.HardClosed, actorUserId: 7); // re-close

        // Exactly one reversal (the second close skips the already-reversed accrual).
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.ReversalOfEntryId == accrual.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Close_AccrualWithNoNextPeriod_Throws()
    {
        using var db = await SeedAsync(); // single full-year period → no period covers 2027-01-01
        await PostAccrualAsync(db, new DateOnly(2026, 6, 15));

        var act = async () => await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*create the next period first*");
    }

    // ─────────────────── close-checklist gate + late-posting (§12) ───────────────────

    private sealed class FakeChecklist(bool pass) : IPeriodCloseChecklistService
    {
        public Task<CloseChecklistResult> EvaluateAsync(int bookId, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(new CloseChecklistResult(bookId, asOf,
                [new CloseChecklistItem("GRNI_RECONCILED", "GRNI ties", pass, pass ? "Reconciled" : "Variance 50.00")]));
    }

    private static FiscalPeriodCloseService ServiceWith(AppDbContext db, IPeriodCloseChecklistService cl)
        => new(db, new SystemClock(), Engine(db), cl);

    [Fact]
    public async Task HardClose_BlockedWhenChecklistFails()
    {
        using var db = await SeedAsync();
        var svc = ServiceWith(db, new FakeChecklist(pass: false));

        var act = async () => await svc.TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed, actorUserId: 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pre-close checklist not satisfied*");

        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.Open);
    }

    [Fact]
    public async Task HardClose_AllowedWhenChecklistPasses()
    {
        using var db = await SeedAsync();
        var svc = ServiceWith(db, new FakeChecklist(pass: true));

        await svc.TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed, actorUserId: 7);

        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task SoftClose_NotGatedByChecklist()
    {
        using var db = await SeedAsync();
        var svc = ServiceWith(db, new FakeChecklist(pass: false)); // would block a hard-close

        await svc.TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed, actorUserId: 7);

        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.SoftClosed);
    }

    [Fact]
    public async Task Checklist_CleanBook_AllPassed()
    {
        using var db = await SeedAsync();
        var checklist = new PeriodCloseChecklistService(
            new GrniReconciliationService(db, new SystemClock()),
            new ArAgingService(db, new SystemClock()),
            new ApAgingService(db, new SystemClock()));

        var result = await checklist.EvaluateAsync(BookId, new DateOnly(2026, 12, 31));

        result.AllPassed.Should().BeTrue();
        result.Items.Should().Contain(i => i.Key == "GRNI_RECONCILED" && i.Passed);
    }

    [Fact]
    public async Task LatePosting_OpenPeriod_KeepsDesiredDate()
    {
        using var db = await SeedTwoPeriodsAsync();
        var resolver = new PostingDateResolver(db);

        var date = await resolver.ResolveOpenPostingDateAsync(BookId, new DateOnly(2026, 1, 15));

        date.Should().Be(new DateOnly(2026, 1, 15)); // Jan is open
    }

    [Fact]
    public async Task LatePosting_ClosedPeriod_CatchesUpToNextOpenPeriodStart()
    {
        using var db = await SeedTwoPeriodsAsync();
        await Service(db).TransitionAsync(JanId, FiscalPeriodStatus.HardClosed, actorUserId: 7);
        var resolver = new PostingDateResolver(db);

        var date = await resolver.ResolveOpenPostingDateAsync(BookId, new DateOnly(2026, 1, 15));

        date.Should().Be(new DateOnly(2026, 2, 1)); // Jan closed → catch up into Feb (next open period start)
    }
}
