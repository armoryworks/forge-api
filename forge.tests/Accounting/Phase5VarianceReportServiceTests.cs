using FluentAssertions;

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
/// Phase-5 standard costing — variance rollup. Sums each of the six variance accounts (+ production residual)
/// over a date range; the lumped total = SUM(lines). Debit-positive = unfavorable, credit = favorable.
/// </summary>
public class Phase5VarianceReportServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    // Variance accounts + a balancing offset.
    private const int PriceId = 510, UsageId = 511, LaborRateId = 513, LaborEffId = 514,
        OhSpendId = 515, OhEffId = 516, ProdId = 512, OffsetId = 260;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book { Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId, ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });

        GlAccount Expense(int id, string num, string name) => new() { Id = id, BookId = BookId, AccountNumber = num, Name = name, AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true };
        db.Set<GlAccount>().AddRange(
            Expense(PriceId, "51000", "Purchase Price Variance"),
            Expense(UsageId, "51100", "Material Usage Variance"),
            Expense(ProdId, "51200", "Production Variance"),
            Expense(LaborRateId, "51300", "Labor Rate Variance"),
            Expense(LaborEffId, "51310", "Labor Efficiency Variance"),
            Expense(OhSpendId, "51320", "Overhead Spending Variance"),
            Expense(OhEffId, "51330", "Overhead Efficiency Variance"),
            new GlAccount { Id = OffsetId, BookId = BookId, AccountNumber = "26000", Name = "Accrued", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "PURCHASE_PRICE_VARIANCE", GlAccountId = PriceId },
            new AccountDeterminationRule { BookId = BookId, Key = "MATERIAL_USAGE_VARIANCE", GlAccountId = UsageId },
            new AccountDeterminationRule { BookId = BookId, Key = "PRODUCTION_VARIANCE", GlAccountId = ProdId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_RATE_VARIANCE", GlAccountId = LaborRateId },
            new AccountDeterminationRule { BookId = BookId, Key = "LABOR_EFFICIENCY_VARIANCE", GlAccountId = LaborEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_SPENDING_VARIANCE", GlAccountId = OhSpendId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_EFFICIENCY_VARIANCE", GlAccountId = OhEffId },
            new AccountDeterminationRule { BookId = BookId, Key = "ACCRUED_EXPENSE", GlAccountId = OffsetId });
        await db.SaveChangesAsync();
        return db;
    }

    // Posts a balanced entry hitting the variance accounts (signed amounts: + = debit/unfavorable).
    private static Task PostVariancesAsync(AppDbContext db, DateOnly date,
        decimal price, decimal usage, decimal labRate, decimal labEff, decimal ohSpend, decimal ohEff, decimal prod)
    {
        var lines = new List<PostingLine>();
        void Add(string key, decimal amt) { if (amt > 0) lines.Add(new PostingLine { AccountKey = key, Debit = amt }); else if (amt < 0) lines.Add(new PostingLine { AccountKey = key, Credit = -amt }); }
        Add("PURCHASE_PRICE_VARIANCE", price);
        Add("MATERIAL_USAGE_VARIANCE", usage);
        Add("LABOR_RATE_VARIANCE", labRate);
        Add("LABOR_EFFICIENCY_VARIANCE", labEff);
        Add("OVERHEAD_SPENDING_VARIANCE", ohSpend);
        Add("OVERHEAD_EFFICIENCY_VARIANCE", ohEff);
        Add("PRODUCTION_VARIANCE", prod);
        var net = price + usage + labRate + labEff + ohSpend + ohEff + prod;
        // Offset to balance: net debit → credit accrued (and vice-versa).
        if (net > 0) lines.Add(new PostingLine { AccountKey = "ACCRUED_EXPENSE", Credit = net });
        else if (net < 0) lines.Add(new PostingLine { AccountKey = "ACCRUED_EXPENSE", Debit = -net });

        return Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = date, Source = JournalSource.Inventory, CurrencyId = UsdId,
            IdempotencyKey = $"var:{date:yyyyMMdd}", Lines = lines,
        }, 7);
    }

    [Fact]
    public async Task Rollup_SumsEachVariance_AndTotalsToLumped()
    {
        using var db = await SeedAsync();
        await PostVariancesAsync(db, new DateOnly(2026, 1, 15),
            price: 10m, usage: 5m, labRate: -3m, labEff: 4m, ohSpend: 6m, ohEff: -2m, prod: 1m);

        var report = await new VarianceReportService(db)
            .GetAsync(BookId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        report.Lines.Single(l => l.Key == "PURCHASE_PRICE_VARIANCE").Amount.Should().Be(10m);
        report.Lines.Single(l => l.Key == "MATERIAL_USAGE_VARIANCE").Amount.Should().Be(5m);
        report.Lines.Single(l => l.Key == "LABOR_RATE_VARIANCE").Amount.Should().Be(-3m);
        report.Lines.Single(l => l.Key == "LABOR_RATE_VARIANCE").IsFavorable.Should().BeTrue();
        report.Lines.Single(l => l.Key == "LABOR_EFFICIENCY_VARIANCE").Amount.Should().Be(4m);
        report.Lines.Single(l => l.Key == "OVERHEAD_SPENDING_VARIANCE").Amount.Should().Be(6m);
        report.Lines.Single(l => l.Key == "OVERHEAD_EFFICIENCY_VARIANCE").Amount.Should().Be(-2m);
        report.Lines.Single(l => l.Key == "PRODUCTION_VARIANCE").Amount.Should().Be(1m);

        report.Total.Should().Be(21m, "lumped = sum of the slots (10+5−3+4+6−2+1)");
        report.Lines.Should().HaveCount(7);
    }

    [Fact]
    public async Task Rollup_ExcludesActivityOutsideTheDateRange()
    {
        using var db = await SeedAsync();
        await PostVariancesAsync(db, new DateOnly(2026, 1, 15), 10m, 0m, 0m, 0m, 0m, 0m, 0m); // in range
        await PostVariancesAsync(db, new DateOnly(2026, 2, 15), 99m, 0m, 0m, 0m, 0m, 0m, 0m); // out of range

        var report = await new VarianceReportService(db)
            .GetAsync(BookId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        report.Lines.Single(l => l.Key == "PURCHASE_PRICE_VARIANCE").Amount.Should().Be(10m);
        report.Total.Should().Be(10m);
    }
}
