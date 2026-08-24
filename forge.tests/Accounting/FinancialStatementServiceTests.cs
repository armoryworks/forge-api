using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-1 STAGE E — Profit &amp; Loss and Balance Sheet built over the ledger
/// (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance Sheet", §5.3). Proves:
/// the P&amp;L nets Income (credit-normal) / Expense (debit-normal) accounts over a
/// period range (incl. a contra-revenue account) and computes net income; the
/// balance sheet projects Asset/Liability/Equity as of a date, folds in a computed
/// current-year-earnings equity line, and balances (Assets = Liabilities + Equity
/// incl. CY earnings); reads are filter-immune; and both statements carry the
/// Phase-1 incomplete-margin caveat (CogsPosted = false).
/// </summary>
public class FinancialStatementServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    // Account ids
    private const int CashId = 100;
    private const int ArControlId = 102;
    private const int ApControlId = 200;
    private const int RetainedEarningsId = 300;
    private const int CommonStockId = 301;
    private const int RevenueId = 400;
    private const int SalesReturnsId = 401; // contra-revenue (Income, debit-normal)
    private const int RentExpenseId = 600;
    private const int CogsId = 500;

    private const int CustomerAId = 7001;

    private const int OpenPeriodId = 1000;

    /// <summary>As-of / "today" anchor used across the statement tests.</summary>
    private static readonly DateOnly AsOf = new(2026, 6, 30);

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

    private static IClock ClockAsOf()
        => new FixedClock(new DateTimeOffset(AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), ClockAsOf());

    private static FinancialStatementService Service(AppDbContext db)
        => new(db, ClockAsOf());

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
        // One Open period spanning the whole year so any 2026 EntryDate resolves.
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalPeriodStatus.Open,
        });

        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ArControlId, BookId = BookId, AccountNumber = "11000", Name = "Accounts Receivable", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true },
            new GlAccount { Id = ApControlId, BookId = BookId, AccountNumber = "20000", Name = "Accounts Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true },
            new GlAccount { Id = RetainedEarningsId, BookId = BookId, AccountNumber = "30000", Name = "Retained Earnings", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CommonStockId, BookId = BookId, AccountNumber = "31000", Name = "Common Stock", AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesReturnsId, BookId = BookId, AccountNumber = "41000", Name = "Sales Returns & Allowances", AccountType = AccountType.Income, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = CogsId, BookId = BookId, AccountNumber = "50000", Name = "Cost of Goods Sold", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RentExpenseId, BookId = BookId, AccountNumber = "60000", Name = "Rent Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });

        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "AP_CONTROL", GlAccountId = ApControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_RETURNS", GlAccountId = SalesReturnsId },
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "RETAINED_EARNINGS", GlAccountId = RetainedEarningsId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = RentExpenseId },
            new AccountDeterminationRule { BookId = BookId, Key = "COGS", GlAccountId = CogsId });

        db.Set<Customer>().Add(new Customer { Id = CustomerAId, Name = "Acme Corp" });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Posts a two-line balanced entry by GL account id, dated <paramref name="date"/>.</summary>
    private static Task PostAsync(
        AppDbContext db, int debitAccountId, int creditAccountId, decimal amount, DateOnly date,
        string source, SubledgerPartyType? debitParty = null, int? debitPartyId = null,
        SubledgerPartyType? creditParty = null, int? creditPartyId = null)
        => Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = date,
            Source = JournalSource.Manual,
            SourceType = source,
            SourceId = null,
            CurrencyId = UsdId,
            // Manual source → idempotency key optional.
            Lines =
            [
                new PostingLine { GlAccountId = debitAccountId, PartyType = debitParty, PartyId = debitPartyId, Debit = amount },
                new PostingLine { GlAccountId = creditAccountId, PartyType = creditParty, PartyId = creditPartyId, Credit = amount },
            ],
        }, postedByUserId: 7);

    // ── Profit & Loss ────────────────────────────────────────────────────────

    [Fact]
    public async Task Pnl_NetsIncomeAndExpense_WithContraRevenue()
    {
        using var db = await SeedAsync();
        // Revenue: Dr AR / Cr Revenue 1000
        await PostAsync(db, ArControlId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);
        // A second revenue posting 500
        await PostAsync(db, ArControlId, RevenueId, 500m, new DateOnly(2026, 3, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);
        // Sales return (contra-revenue, Income account debit): Dr SalesReturns / Cr AR 200
        await PostAsync(db, SalesReturnsId, ArControlId, 200m, new DateOnly(2026, 3, 15), "CreditMemo",
            creditParty: SubledgerPartyType.Customer, creditPartyId: CustomerAId);
        // Rent expense: Dr Rent / Cr Cash 300
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 4, 1), "Expense");

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        // Income side: Revenue +1500, Sales Returns −200 (contra) → total income 1300.
        pnl.Income.Single(l => l.GlAccountId == RevenueId).Amount.Should().Be(1500m);
        pnl.Income.Single(l => l.GlAccountId == SalesReturnsId).Amount.Should().Be(-200m);
        pnl.TotalIncome.Should().Be(1300m);

        // Expense side: Rent 300.
        pnl.Expense.Single(l => l.GlAccountId == RentExpenseId).Amount.Should().Be(300m);
        pnl.TotalExpense.Should().Be(300m);

        // Net income = 1300 − 300 = 1000.
        pnl.NetIncome.Should().Be(1000m);

        // No COGS posted → incomplete-margin label present.
        pnl.CogsPosted.Should().BeFalse();
        pnl.MarginCaveat.Should().Contain("COGS");
    }

    [Fact]
    public async Task Pnl_CogsPosted_DerivedTrue_WhenCogsLineExists()
    {
        using var db = await SeedAsync();
        await PostAsync(db, ArControlId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);
        // A posted COGS-account line (the Phase-2 STAGE B relief) flips CogsPosted true.
        await PostAsync(db, CogsId, CashId, 300m, new DateOnly(2026, 2, 1), "Inventory");

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        pnl.CogsPosted.Should().BeTrue();
        pnl.MarginCaveat.Should().BeEmpty();
    }

    [Fact]
    public async Task Pnl_CogsPosted_FalseWhenCogsPostedThenReversed()
    {
        using var db = await SeedAsync();
        var engine = Engine(db);
        var cogs = await engine.PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 2, 1), Source = JournalSource.Inventory,
            SourceType = "Invoice", SourceId = 1, CurrencyId = UsdId,
            IdempotencyKey = "Inventory:Invoice:1:COGS",
            Lines =
            [
                new PostingLine { GlAccountId = CogsId, Debit = 300m },
                new PostingLine { GlAccountId = CashId, Credit = 300m },
            ],
        }, postedByUserId: 7);

        await engine.ReverseAsync(cogs.Id, new DateOnly(2026, 2, 2), "correction", reversedByUserId: 7);

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        // Posted then reversed → net COGS zero → not "live" → caveat returns.
        pnl.CogsPosted.Should().BeFalse();
        pnl.MarginCaveat.Should().Contain("COGS");
    }

    [Fact]
    public async Task Pnl_CogsPosted_FalseForWindowWithoutCogs()
    {
        using var db = await SeedAsync();
        await PostAsync(db, ArControlId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);
        await PostAsync(db, CogsId, CashId, 300m, new DateOnly(2026, 2, 1), "Inventory");

        // A later window with revenue activity dates but no COGS posted IN it must still warn.
        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        pnl.CogsPosted.Should().BeFalse();
        pnl.MarginCaveat.Should().Contain("COGS");
    }

    [Fact]
    public async Task Pnl_RespectsPeriodRange()
    {
        using var db = await SeedAsync();
        await PostAsync(db, RentExpenseId, CashId, 100m, new DateOnly(2026, 1, 10), "Expense"); // in
        await PostAsync(db, RentExpenseId, CashId, 200m, new DateOnly(2026, 5, 10), "Expense"); // out (after toDate)

        var pnl = await Service(db).GetProfitAndLossAsync(
            BookId, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        pnl.TotalExpense.Should().Be(100m);
        pnl.NetIncome.Should().Be(-100m);
    }

    [Fact]
    public async Task Pnl_ExcludesBalanceSheetAccounts()
    {
        using var db = await SeedAsync();
        // A pure balance-sheet entry: Dr Cash / Cr Common Stock (no P&L impact).
        await PostAsync(db, CashId, CommonStockId, 5000m, new DateOnly(2026, 1, 5), "Capital");
        await PostAsync(db, ArControlId, RevenueId, 700m, new DateOnly(2026, 2, 5), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        // Only the revenue shows; cash/equity are balance-sheet accounts.
        pnl.Income.Should().ContainSingle(l => l.GlAccountId == RevenueId);
        pnl.TotalIncome.Should().Be(700m);
        pnl.Expense.Should().BeEmpty();
        pnl.Income.Should().NotContain(l => l.GlAccountId == CashId || l.GlAccountId == CommonStockId);
    }

    [Fact]
    public async Task Pnl_ReversedEntryNetsToZero()
    {
        using var db = await SeedAsync();
        await PostAsync(db, RentExpenseId, CashId, 400m, new DateOnly(2026, 2, 1), "Expense");
        var entry = await db.JournalEntries
            .IgnoreQueryFilters()
            .FirstAsync(e => e.SourceType == "Expense");
        await Engine(db).ReverseAsync(entry.Id, new DateOnly(2026, 2, 2), "correction", reversedByUserId: 7);

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        // Original (now Reversed) + its (Posted) reversal net to zero.
        pnl.TotalExpense.Should().Be(0m);
        pnl.Expense.Should().BeEmpty();
    }

    // ── Balance Sheet ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BalanceSheet_ClassifiesAndBalances_WithCurrentYearEarnings()
    {
        using var db = await SeedAsync();
        // Opening capital: Dr Cash 5000 / Cr Common Stock 5000.
        await PostAsync(db, CashId, CommonStockId, 5000m, new DateOnly(2026, 1, 1), "Capital");
        // Revenue on account: Dr AR 1000 / Cr Revenue 1000.
        await PostAsync(db, ArControlId, RevenueId, 1000m, new DateOnly(2026, 2, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);
        // Operating expense paid in cash: Dr Rent 300 / Cr Cash 300.
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 3, 1), "Expense");
        // Buy something on account: Dr Rent 100 / Cr AP 100 (vendor).
        await PostAsync(db, RentExpenseId, ApControlId, 100m, new DateOnly(2026, 3, 5), "Bill",
            creditParty: SubledgerPartyType.Vendor, creditPartyId: 9001);

        var bs = await Service(db).GetBalanceSheetAsync(BookId, AsOf);

        // Assets: Cash = 5000 − 300 = 4700; AR = 1000 → total 5700.
        bs.Assets.Single(l => l.GlAccountId == CashId).Amount.Should().Be(4700m);
        bs.Assets.Single(l => l.GlAccountId == ArControlId).Amount.Should().Be(1000m);
        bs.TotalAssets.Should().Be(5700m);

        // Liabilities: AP = 100.
        bs.TotalLiabilities.Should().Be(100m);

        // Posted equity: Common Stock 5000 (Retained Earnings still 0 — no close yet).
        bs.TotalEquityPosted.Should().Be(5000m);

        // Current-year earnings = revenue 1000 − expense (300 + 100) = 600.
        bs.CurrentYearEarnings.Should().Be(600m);

        // Equity incl. earnings = 5000 + 600 = 5600; L+E = 100 + 5600 = 5700 = assets.
        bs.TotalEquityWithEarnings.Should().Be(5600m);
        bs.TotalLiabilitiesAndEquity.Should().Be(5700m);
        bs.IsBalanced.Should().BeTrue();

        // Phase-1 incomplete-margin label.
        bs.CogsPosted.Should().BeFalse();
        bs.MarginCaveat.Should().Contain("COGS");
    }

    [Fact]
    public async Task BalanceSheet_AsOfDate_ExcludesLaterActivity()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");
        // This revenue is dated AFTER the as-of date and must be excluded.
        await PostAsync(db, ArControlId, RevenueId, 999m, new DateOnly(2026, 7, 15), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);

        var bs = await Service(db).GetBalanceSheetAsync(BookId, AsOf); // 2026-06-30

        bs.TotalAssets.Should().Be(1000m);          // only the cash from capital
        bs.CurrentYearEarnings.Should().Be(0m);     // the later revenue is excluded
        bs.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task BalanceSheet_NoFiscalYearCoveringDate_CurrentYearEarningsZero()
    {
        using var db = await SeedAsync();
        // Activity dated in 2026 but ask for a balance sheet in 2030 (no FY covers it).
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");
        await PostAsync(db, ArControlId, RevenueId, 500m, new DateOnly(2026, 2, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);

        var bs = await Service(db).GetBalanceSheetAsync(BookId, new DateOnly(2030, 1, 1));

        // No fiscal year contains 2030-01-01 → current-year-earnings is 0 (we don't
        // guess a window). Assets still reflect all activity on/before the date.
        bs.CurrentYearEarnings.Should().Be(0m);
        bs.TotalAssets.Should().Be(1500m); // cash 1000 + AR 500
    }

    [Fact]
    public async Task BalanceSheet_IsFilterImmune_SoftDeletedLedgerRowStillCounts()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, CommonStockId, 2500m, new DateOnly(2026, 1, 1), "Capital");

        // Soft-delete the customer master referenced elsewhere shouldn't matter, but
        // assert the statement reads ignore the global filter by soft-deleting the
        // customer and confirming totals are intact.
        var customer = await db.Set<Customer>().FirstAsync(c => c.Id == CustomerAId);
        customer.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var bs = await Service(db).GetBalanceSheetAsync(BookId, AsOf);

        bs.TotalAssets.Should().Be(2500m);
        bs.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task BalanceSheet_DefaultsAsOfToClockToday()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");

        var bs = await Service(db).GetBalanceSheetAsync(BookId); // no asOf → clock = AsOf

        bs.AsOfDate.Should().Be(AsOf);
        bs.TotalAssets.Should().Be(1000m);
    }

    // ── Comparative period ───────────────────────────────────────────────────

    [Fact]
    public async Task Pnl_NoComparison_PriorAndVarianceNull()
    {
        using var db = await SeedAsync();
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 2, 1), "Expense");

        var pnl = await Service(db).GetProfitAndLossAsync(BookId, new DateOnly(2026, 1, 1), AsOf);

        pnl.HasComparison.Should().BeFalse();
        pnl.CompareFromDate.Should().BeNull();
        pnl.PriorTotalExpense.Should().BeNull();
        var rent = pnl.Expense.Single(l => l.GlAccountId == RentExpenseId);
        rent.PriorAmount.Should().BeNull();
        rent.Variance.Should().BeNull();
        rent.VariancePercent.Should().BeNull();
        pnl.TotalExpenseVariance.Should().BeNull();
    }

    [Fact]
    public async Task Pnl_ExplicitCompareRange_ComputesVarianceAndPercent()
    {
        using var db = await SeedAsync();
        // Prior window (Q1): rent 100. Current window (Q2): rent 300.
        await PostAsync(db, RentExpenseId, CashId, 100m, new DateOnly(2026, 2, 1), "Expense");
        await PostAsync(db, RentExpenseId, CashId, 300m, new DateOnly(2026, 5, 1), "Expense");

        var pnl = await Service(db).GetProfitAndLossAsync(
            BookId,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30),
            compare: true,
            compareFromDate: new DateOnly(2026, 1, 1), compareToDate: new DateOnly(2026, 3, 31));

        pnl.HasComparison.Should().BeTrue();
        pnl.CompareFromDate.Should().Be(new DateOnly(2026, 1, 1));
        pnl.CompareToDate.Should().Be(new DateOnly(2026, 3, 31));

        var rent = pnl.Expense.Single(l => l.GlAccountId == RentExpenseId);
        rent.Amount.Should().Be(300m);
        rent.PriorAmount.Should().Be(100m);
        rent.Variance.Should().Be(200m);
        rent.VariancePercent.Should().Be(200m); // (300−100)/100 × 100

        pnl.PriorTotalExpense.Should().Be(100m);
        pnl.TotalExpenseVariance.Should().Be(200m);
        pnl.TotalExpenseVariancePercent.Should().Be(200m);
        pnl.PriorNetIncome.Should().Be(-100m);
        pnl.NetIncomeVariance.Should().Be(-200m); // net −300 vs prior −100
    }

    [Fact]
    public async Task Pnl_Comparison_PriorZero_VariancePercentNull_DivideByZeroGuard()
    {
        using var db = await SeedAsync();
        // Revenue only in the current window; nothing in the prior window.
        await PostAsync(db, ArControlId, RevenueId, 500m, new DateOnly(2026, 5, 1), "Invoice",
            debitParty: SubledgerPartyType.Customer, debitPartyId: CustomerAId);

        var pnl = await Service(db).GetProfitAndLossAsync(
            BookId,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30),
            compare: true,
            compareFromDate: new DateOnly(2026, 1, 1), compareToDate: new DateOnly(2026, 3, 31));

        var rev = pnl.Income.Single(l => l.GlAccountId == RevenueId);
        rev.Amount.Should().Be(500m);
        rev.PriorAmount.Should().Be(0m);          // present now, absent prior → 0, not null
        rev.Variance.Should().Be(500m);
        rev.VariancePercent.Should().BeNull();    // prior 0 → guarded divide-by-zero → null
    }

    [Fact]
    public async Task Pnl_Comparison_AccountOnlyInPrior_ShowsAsCurrentZeroLine()
    {
        using var db = await SeedAsync();
        // Rent only in the PRIOR window; none in current.
        await PostAsync(db, RentExpenseId, CashId, 400m, new DateOnly(2026, 2, 1), "Expense");

        var pnl = await Service(db).GetProfitAndLossAsync(
            BookId,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30),
            compare: true,
            compareFromDate: new DateOnly(2026, 1, 1), compareToDate: new DateOnly(2026, 3, 31));

        var rent = pnl.Expense.Single(l => l.GlAccountId == RentExpenseId);
        rent.Amount.Should().Be(0m);              // dropped to nothing this period, still visible
        rent.PriorAmount.Should().Be(400m);
        rent.Variance.Should().Be(-400m);
        rent.VariancePercent.Should().Be(-100m);  // (0−400)/400 × 100
    }

    [Fact]
    public async Task Pnl_Comparison_DerivesImmediatelyPrecedingEqualLengthPeriod()
    {
        using var db = await SeedAsync();
        // Window chosen so the derived prior period + its just-before edge all fall
        // inside the seeded FY2026 (the posting engine requires a fiscal period).
        var curFrom = new DateOnly(2026, 5, 1);
        var curTo = new DateOnly(2026, 6, 30);
        // Same derivation the service uses: prior ends the day before current starts,
        // same inclusive length.
        var priorTo = curFrom.AddDays(-1);
        var priorFrom = priorTo.AddDays(-(curTo.DayNumber - curFrom.DayNumber));

        await PostAsync(db, RentExpenseId, CashId, 300m, curFrom, "Expense");            // current
        await PostAsync(db, RentExpenseId, CashId, 120m, priorTo, "Expense");            // prior (edge)
        await PostAsync(db, RentExpenseId, CashId, 80m, priorFrom, "Expense");           // prior (edge)
        await PostAsync(db, RentExpenseId, CashId, 999m, priorFrom.AddDays(-1), "Expense"); // just before → excluded

        var pnl = await Service(db).GetProfitAndLossAsync(
            BookId, curFrom, curTo, compare: true); // no explicit compare range → derive

        pnl.HasComparison.Should().BeTrue();
        pnl.CompareFromDate.Should().Be(priorFrom);
        pnl.CompareToDate.Should().Be(priorTo);
        pnl.TotalExpense.Should().Be(300m);
        pnl.PriorTotalExpense.Should().Be(200m); // 120 + 80, the 999 is out of window
    }

    [Fact]
    public async Task BalanceSheet_ExplicitCompareAsOf_ComputesVariance()
    {
        using var db = await SeedAsync();
        // 1000 of cash by prior date; +500 more before the current date.
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");
        await PostAsync(db, CashId, CommonStockId, 500m, new DateOnly(2026, 5, 1), "Capital");

        var bs = await Service(db).GetBalanceSheetAsync(
            BookId, AsOf, compare: true, compareAsOfDate: new DateOnly(2026, 4, 30)); // 2026-06-30 vs 2026-04-30

        bs.HasComparison.Should().BeTrue();
        bs.CompareAsOfDate.Should().Be(new DateOnly(2026, 4, 30));

        var cash = bs.Assets.Single(l => l.GlAccountId == CashId);
        cash.Amount.Should().Be(1500m);
        cash.PriorAmount.Should().Be(1000m);
        cash.Variance.Should().Be(500m);
        cash.VariancePercent.Should().Be(50m);

        bs.TotalAssets.Should().Be(1500m);
        bs.PriorTotalAssets.Should().Be(1000m);
        bs.TotalAssetsVariance.Should().Be(500m);
        bs.TotalAssetsVariancePercent.Should().Be(50m);
    }

    [Fact]
    public async Task BalanceSheet_Comparison_DefaultsToOneYearPrior()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");

        var bs = await Service(db).GetBalanceSheetAsync(BookId, AsOf, compare: true); // no compareAsOf

        bs.HasComparison.Should().BeTrue();
        bs.CompareAsOfDate.Should().Be(AsOf.AddYears(-1)); // 2025-06-30
        // The 2026-01-01 capital is after the 2025 prior date → prior assets 0.
        bs.PriorTotalAssets.Should().Be(0m);
        bs.TotalAssets.Should().Be(1000m);
        bs.TotalAssetsVariance.Should().Be(1000m);
        bs.TotalAssetsVariancePercent.Should().BeNull(); // prior 0 → guarded → null
    }

    [Fact]
    public async Task BalanceSheet_NoComparison_PriorAndVarianceNull()
    {
        using var db = await SeedAsync();
        await PostAsync(db, CashId, CommonStockId, 1000m, new DateOnly(2026, 1, 1), "Capital");

        var bs = await Service(db).GetBalanceSheetAsync(BookId, AsOf);

        bs.HasComparison.Should().BeFalse();
        bs.CompareAsOfDate.Should().BeNull();
        bs.PriorTotalAssets.Should().BeNull();
        bs.TotalAssetsVariance.Should().BeNull();
        bs.Assets.Single(l => l.GlAccountId == CashId).PriorAmount.Should().BeNull();
    }
}
