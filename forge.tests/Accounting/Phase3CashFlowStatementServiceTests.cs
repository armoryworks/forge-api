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
/// Phase-3 — indirect-method Cash-Flow statement. The invariant: NetChangeInCash always reconciles to the
/// actual cash-account movement (the double-entry identity), across cash sales, working-capital changes
/// (receivable/accrual), financing (contributions/distributions), and after a year-end close (the RE roll is
/// excluded so net income isn't double-counted).
/// </summary>
public class Phase3CashFlowStatementServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;

    private const int CashId = 100;
    private const int SalesId = 101;
    private const int ExpenseId = 102;
    private const int ReceivableId = 103;
    private const int AccruedId = 104;
    private const int StockId = 105;
    private const int ReId = 106;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static CashFlowStatementService Service(AppDbContext db) => new(db, new SystemClock());

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
            Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = From, EndDate = To, Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = From, EndDate = To, Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ReceivableId, BookId = BookId, AccountNumber = "11500", Name = "Other Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccruedId, BookId = BookId, AccountNumber = "23000", Name = "Accrued Liability", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = StockId, BookId = BookId, AccountNumber = "31000", Name = "Common Stock", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ReId, BookId = BookId, AccountNumber = "32000", Name = "Retained Earnings", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = SalesId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "OTHER_AR", GlAccountId = ReceivableId },
            new AccountDeterminationRule { BookId = BookId, Key = "ACCRUED", GlAccountId = AccruedId },
            new AccountDeterminationRule { BookId = BookId, Key = "COMMON_STOCK", GlAccountId = StockId },
            new AccountDeterminationRule { BookId = BookId, Key = "RETAINED_EARNINGS", GlAccountId = ReId });

        await db.SaveChangesAsync();
        return db;
    }

    private static int _seq;
    private static Task PostAsync(AppDbContext db, string drKey, string crKey, decimal amount, string? sourceType = null)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 6, 30),
            Source = sourceType == "YearEndClose" ? JournalSource.System : JournalSource.Manual,
            SourceType = sourceType, SourceId = sourceType is null ? null : 1,
            CurrencyId = UsdId, IdempotencyKey = $"cf:{_seq++}",
            Lines =
            [
                new PostingLine { AccountKey = drKey, Debit = amount, Description = "dr" },
                new PostingLine { AccountKey = crKey, Credit = amount, Description = "cr" },
            ],
        }, 7);

    [Fact]
    public async Task CashSale_AllOperating_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "CASH", "SALES_REVENUE", 1000m);

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.NetIncome.Should().Be(1000m);
        r.NetCashFromOperating.Should().Be(1000m);
        r.ActualCashChange.Should().Be(1000m);
        r.NetChangeInCash.Should().Be(1000m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task CreditSale_ReceivableIncrease_UsesCash_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "OTHER_AR", "SALES_REVENUE", 1000m); // revenue but no cash; receivable up

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.NetIncome.Should().Be(1000m);
        r.OperatingAdjustments.Single(l => l.GlAccountId == ReceivableId).Amount.Should().Be(-1000m); // use of cash
        r.NetCashFromOperating.Should().Be(0m);
        r.ActualCashChange.Should().Be(0m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task AccruedExpense_LiabilityIncrease_SourceOfCash_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "OPERATING_EXPENSE", "ACCRUED", 600m); // expense but no cash; accrual up

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.NetIncome.Should().Be(-600m);
        r.OperatingAdjustments.Single(l => l.GlAccountId == AccruedId).Amount.Should().Be(600m); // source of cash
        r.NetCashFromOperating.Should().Be(0m);
        r.ActualCashChange.Should().Be(0m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task OwnerContribution_IsFinancing_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "CASH", "COMMON_STOCK", 5000m);

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.NetIncome.Should().Be(0m);
        r.Financing.Single(l => l.GlAccountId == StockId).Amount.Should().Be(5000m); // source
        r.NetCashFromFinancing.Should().Be(5000m);
        r.NetCashFromOperating.Should().Be(0m);
        r.NetChangeInCash.Should().Be(5000m);
        r.ActualCashChange.Should().Be(5000m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task Dividend_IsFinancingOutflow_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "RETAINED_EARNINGS", "CASH", 200m); // distribution

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.Financing.Single(l => l.GlAccountId == ReId).Amount.Should().Be(-200m); // use
        r.NetChangeInCash.Should().Be(-200m);
        r.ActualCashChange.Should().Be(-200m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task AfterYearEndClose_RollExcluded_NetIncomeNotDoubleCounted_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "CASH", "SALES_REVENUE", 1000m);                 // cash revenue
        await PostAsync(db, "SALES_REVENUE", "RETAINED_EARNINGS", 1000m, "YearEndClose"); // the RE roll

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        r.NetIncome.Should().Be(1000m);                 // real income, roll excluded
        r.NetCashFromFinancing.Should().Be(0m);         // the RE roll is NOT a financing flow
        r.NetChangeInCash.Should().Be(1000m);
        r.ActualCashChange.Should().Be(1000m);
        r.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task MixedActivity_Reconciles()
    {
        using var db = await SeedAsync();
        await PostAsync(db, "CASH", "SALES_REVENUE", 1000m);       // +1000 cash, income
        await PostAsync(db, "OTHER_AR", "SALES_REVENUE", 400m);    // income, receivable up (no cash)
        await PostAsync(db, "OPERATING_EXPENSE", "CASH", 300m);    // -300 cash, expense
        await PostAsync(db, "OPERATING_EXPENSE", "ACCRUED", 250m); // expense, accrual up (no cash)
        await PostAsync(db, "CASH", "COMMON_STOCK", 5000m);        // +5000 cash, financing

        var r = await Service(db).GetCashFlowStatementAsync(BookId, From, To);

        // Actual cash = 1000 - 300 + 5000 = 5700.
        r.ActualCashChange.Should().Be(5700m);
        r.NetChangeInCash.Should().Be(5700m);
        r.IsReconciled.Should().BeTrue();
        // Net income = 1000 + 400 - 300 - 250 = 850.
        r.NetIncome.Should().Be(850m);
    }
}
