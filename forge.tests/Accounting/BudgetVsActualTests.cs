using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Budget-vs-actual math over the InMemory provider. Actuals flow through the real
/// <see cref="FinancialStatementService"/> (the shared P&amp;L ledger projection), so
/// these prove the comparison end-to-end: variance = actual − budget and variance%
/// against |budget| (null when budget is zero), the union of budgeted-with-no-actual
/// and actual-with-no-budget accounts, month vs full-year windowing, and the upsert's
/// app-level one-row-per-slot behaviour.
/// </summary>
public class BudgetVsActualTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int CashId = 100;
    private const int ArControlId = 102;
    private const int RevenueId = 400;
    private const int RentExpenseId = 600;
    private const int SuppliesExpenseId = 610; // budgeted account with no activity

    private const int CustomerAId = 7001;

    private static readonly DateOnly AsOf = new(2026, 12, 31);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static IClock Clock()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), Clock());

    private static FinancialStatementService Statements(AppDbContext db)
        => new(db, Clock());

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
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RentExpenseId, BookId = BookId, AccountNumber = "60000", Name = "Rent Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SuppliesExpenseId, BookId = BookId, AccountNumber = "61000", Name = "Shop Supplies", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<Customer>().Add(new Customer { Id = CustomerAId, Name = "Acme Corp" });

        await db.SaveChangesAsync();
        return db;
    }

    private static Task PostAsync(AppDbContext db, int debitAccountId, int creditAccountId, decimal amount, DateOnly date, string source)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = date,
            Source = JournalSource.Manual,
            SourceType = source,
            CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = debitAccountId, Debit = amount },
                new PostingLine { GlAccountId = creditAccountId, Credit = amount },
            ],
        }, postedByUserId: 7);

    private static Task<BudgetLineModel> UpsertBudgetAsync(AppDbContext db, int glAccountId, int? month, decimal amount, int year = 2026)
        => new UpsertBudgetHandler(db).Handle(
            new UpsertBudgetCommand(new UpsertBudgetRequestModel(BookId, glAccountId, year, month, amount)),
            CancellationToken.None);

    private static Task<BudgetVsActual> BudgetVsActualAsync(AppDbContext db, int? month = null, int year = 2026)
        => new GetBudgetVsActualHandler(db, Statements(db)).Handle(
            new GetBudgetVsActualQuery(BookId, year, month), CancellationToken.None);

    [Fact]
    public async Task BudgetVsActual_ComputesVarianceAndPercent_ForFullYear()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice");
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 4, 1), "Expense");

        await UpsertBudgetAsync(db, RevenueId, null, 1200m);
        await UpsertBudgetAsync(db, RentExpenseId, null, 250m);

        var result = await BudgetVsActualAsync(db);

        var revenue = result.Lines.Single(l => l.GlAccountId == RevenueId);
        revenue.ActualAmount.Should().Be(1000m);
        revenue.BudgetAmount.Should().Be(1200m);
        revenue.Variance.Should().Be(-200m);
        revenue.VariancePercent.Should().BeApproximately(-16.67m, 0.01m);

        var rent = result.Lines.Single(l => l.GlAccountId == RentExpenseId);
        rent.ActualAmount.Should().Be(300m);
        rent.BudgetAmount.Should().Be(250m);
        rent.Variance.Should().Be(50m);
        rent.VariancePercent.Should().Be(20m);

        result.TotalBudget.Should().Be(1450m);
        result.TotalActual.Should().Be(1300m);
        result.TotalVariance.Should().Be(-150m);
    }

    [Fact]
    public async Task BudgetVsActual_BudgetedAccountWithNoActual_ReadsActualZero_AndResolvesAccountName()
    {
        using var db = await SeedAsync();
        await UpsertBudgetAsync(db, SuppliesExpenseId, null, 500m);

        var result = await BudgetVsActualAsync(db);

        var supplies = result.Lines.Single(l => l.GlAccountId == SuppliesExpenseId);
        supplies.ActualAmount.Should().Be(0m);
        supplies.BudgetAmount.Should().Be(500m);
        supplies.Variance.Should().Be(-500m);
        // Name/number resolved via the GlAccounts fallback lookup (no P&L line existed).
        supplies.AccountNumber.Should().Be("61000");
        supplies.AccountName.Should().Be("Shop Supplies");
    }

    [Fact]
    public async Task BudgetVsActual_ActualWithNoBudget_ReadsBudgetZero_AndNullPercent()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice");

        var result = await BudgetVsActualAsync(db);

        var revenue = result.Lines.Single(l => l.GlAccountId == RevenueId);
        revenue.BudgetAmount.Should().Be(0m);
        revenue.ActualAmount.Should().Be(1000m);
        revenue.Variance.Should().Be(1000m);
        revenue.VariancePercent.Should().BeNull(); // divide-by-zero guard: |budget| == 0
    }

    [Fact]
    public async Task BudgetVsActual_MonthScope_UsesMonthlyBudgetAndMonthWindow()
    {
        using var db = await SeedAsync();
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 2, 10), "Expense");
        await PostAsync(db, RentExpenseId, CashId, 400m, new DateOnly(2026, 3, 10), "Expense");

        await UpsertBudgetAsync(db, RentExpenseId, 2, 350m); // February monthly budget

        var result = await BudgetVsActualAsync(db, month: 2);

        result.FromDate.Should().Be(new DateOnly(2026, 2, 1));
        result.ToDate.Should().Be(new DateOnly(2026, 2, 28));

        var rent = result.Lines.Single(l => l.GlAccountId == RentExpenseId);
        rent.ActualAmount.Should().Be(300m); // only February's posting is in-window
        rent.BudgetAmount.Should().Be(350m);
        rent.Variance.Should().Be(-50m);
    }

    [Fact]
    public async Task Upsert_SecondCallForSameSlot_UpdatesInPlace_NoDuplicateRow()
    {
        using var db = await SeedAsync();

        await UpsertBudgetAsync(db, RevenueId, null, 100m);
        var updated = await UpsertBudgetAsync(db, RevenueId, null, 250m);
        updated.Amount.Should().Be(250m);

        var rows = await new ListBudgetsHandler(db).Handle(new ListBudgetsQuery(BookId, 2026), CancellationToken.None);
        rows.Should().ContainSingle(r => r.GlAccountId == RevenueId);
        rows.Single(r => r.GlAccountId == RevenueId).Amount.Should().Be(250m);
    }

    [Fact]
    public async Task Upsert_FullYearAndMonthly_AreDistinctSlots()
    {
        using var db = await SeedAsync();

        await UpsertBudgetAsync(db, RentExpenseId, null, 1200m); // annual
        await UpsertBudgetAsync(db, RentExpenseId, 1, 100m);     // January

        var rows = await new ListBudgetsHandler(db).Handle(new ListBudgetsQuery(BookId, 2026), CancellationToken.None);
        rows.Where(r => r.GlAccountId == RentExpenseId).Should().HaveCount(2);
    }
}
