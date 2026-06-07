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
/// Phase-3 — year-end close / Retained-Earnings roll-forward. Proves the closing entry zeroes the P&amp;L
/// accounts into RE (net income → Cr RE / net loss → Dr RE), the year + periods lock, and — critically — the
/// statement interplay: a closed year still reports its real revenue/expense (closing excluded) and the
/// balance sheet shows the roll in RE with zero current-year-earnings (no double-count).
/// </summary>
public class Phase3YearEndCloseServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;

    private const int CashId = 100;
    private const int SalesId = 101;
    private const int ExpenseId = 102;
    private const int ReId = 103;

    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly YearEnd = new(2026, 12, 31);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static YearEndCloseService Service(AppDbContext db) => new(db, Engine(db));
    private static FinancialStatementService Statements(AppDbContext db) => new(db, new SystemClock());

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
            StartDate = YearStart, EndDate = YearEnd, Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = YearStart, EndDate = YearEnd, Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ReId, BookId = BookId, AccountNumber = "32000", Name = "Retained Earnings", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = SalesId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "RETAINED_EARNINGS", GlAccountId = ReId });

        await db.SaveChangesAsync();
        return db;
    }

    private static Task PostAsync(AppDbContext db, string drKey, string crKey, decimal amount, string tag)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 6, 30), Source = JournalSource.Manual, CurrencyId = UsdId,
            IdempotencyKey = $"test:{tag}",
            Lines =
            [
                new PostingLine { AccountKey = drKey, Debit = amount, Description = "dr" },
                new PostingLine { AccountKey = crKey, Credit = amount, Description = "cr" },
            ],
        }, 7);

    /// <summary>Revenue 1000, expense 600 → net income 400.</summary>
    private static async Task SeedActivityAsync(AppDbContext db, decimal revenue = 1000m, decimal expense = 600m)
    {
        if (revenue > 0m) await PostAsync(db, "CASH", "SALES_REVENUE", revenue, "rev");
        if (expense > 0m) await PostAsync(db, "OPERATING_EXPENSE", "CASH", expense, "exp");
    }

    [Fact]
    public async Task Close_NetIncome_RollsToRetainedEarnings()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);

        var result = await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        result.NetIncome.Should().Be(400m);
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "YearEndClose");
        entry.Lines.Single(l => l.GlAccountId == SalesId).Debit.Should().Be(1000m);   // zero the revenue
        entry.Lines.Single(l => l.GlAccountId == ExpenseId).Credit.Should().Be(600m); // zero the expense
        entry.Lines.Single(l => l.GlAccountId == ReId).Credit.Should().Be(400m);      // net income → Cr RE
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Close_NetLoss_DebitsRetainedEarnings()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db, revenue: 600m, expense: 1000m); // net loss 400

        var result = await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        result.NetIncome.Should().Be(-400m);
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.SourceType == "YearEndClose");
        entry.Lines.Single(l => l.GlAccountId == ReId).Debit.Should().Be(400m); // net loss → Dr RE
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Close_LocksYearAndPeriods()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);

        var result = await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        result.PeriodsHardClosed.Should().Be(1);
        (await db.FiscalYears.SingleAsync(y => y.Id == FiscalYearId)).Status.Should().Be(FiscalYearStatus.Closed);
        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task Close_AlreadyClosed_Throws()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);
        await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        var act = async () => await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already closed*");
    }

    [Fact]
    public async Task Close_NoActivity_MarksClosedWithNoEntry()
    {
        using var db = await SeedAsync(); // no P&L activity

        var result = await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        result.JournalEntryId.Should().BeNull();
        result.NetIncome.Should().Be(0m);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        (await db.FiscalYears.SingleAsync(y => y.Id == FiscalYearId)).Status.Should().Be(FiscalYearStatus.Closed);
    }

    [Fact]
    public async Task BeforeClose_BalanceSheet_ShowsCurrentYearEarnings_NotRolledToRe()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);

        var bs = await Statements(db).GetBalanceSheetAsync(BookId, YearEnd);

        bs.CurrentYearEarnings.Should().Be(400m);                       // interim earnings
        bs.Equity.Should().NotContain(e => e.GlAccountId == ReId);      // RE not yet rolled
    }

    [Fact]
    public async Task AfterClose_ProfitAndLoss_StillReportsRealIncome()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);
        await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        var pnl = await Statements(db).GetProfitAndLossAsync(BookId, YearStart, YearEnd);

        // The closing entry is excluded → the closed year still shows its real revenue/expense.
        pnl.TotalIncome.Should().Be(1000m);
        pnl.TotalExpense.Should().Be(600m);
    }

    [Fact]
    public async Task AfterClose_BalanceSheet_RollsToRe_WithNoCurrentYearEarnings()
    {
        using var db = await SeedAsync();
        await SeedActivityAsync(db);
        await Service(db).CloseYearAsync(FiscalYearId, closedByUserId: 7);

        var bs = await Statements(db).GetBalanceSheetAsync(BookId, YearEnd);

        // Earnings now live in RE (rolled), and the interim adjustment is zero — no double-count.
        bs.Equity.Single(e => e.GlAccountId == ReId).Amount.Should().Be(400m);
        bs.CurrentYearEarnings.Should().Be(0m);
    }
}
