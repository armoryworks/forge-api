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
/// Phase-4b — multi-currency posting + unrealized FX revaluation. Proves a foreign-currency entry stores
/// FunctionalAmount = txn × rate, and the period-end reval re-measures the net foreign monetary position
/// (here foreign cash) to a new rate, posting the gain/loss to FX_REVALUATION / FX_GAIN|LOSS with auto-reverse.
/// </summary>
public class Phase4FxRevaluationServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int SalesId = 101;
    private const int FxRevalId = 102;
    private const int FxGainId = 103;
    private const int FxLossId = 104;

    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static FxRevaluationService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10200", Name = "EUR Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SalesId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxRevalId, BookId = BookId, AccountNumber = "13900", Name = "FX Revaluation", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxGainId, BookId = BookId, AccountNumber = "45000", Name = "FX Gain", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = FxLossId, BookId = BookId, AccountNumber = "65000", Name = "FX Loss", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = SalesId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_REVALUATION", GlAccountId = FxRevalId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_GAIN", GlAccountId = FxGainId },
            new AccountDeterminationRule { BookId = BookId, Key = "FX_LOSS", GlAccountId = FxLossId });
        await db.SaveChangesAsync();
        return db;
    }

    // A €100 cash sale booked at the given rate.
    private static Task PostEurCashSaleAsync(AppDbContext db, decimal txn, decimal rate) => Engine(db).PostAsync(new PostingRequest
    {
        BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = EurId, FxRate = rate,
        IdempotencyKey = $"eursale:{rate}",
        Lines =
        [
            new PostingLine { AccountKey = "CASH", Debit = txn, Description = "EUR cash" },
            new PostingLine { AccountKey = "SALES_REVENUE", Credit = txn, Description = "EUR sale" },
        ],
    }, 7);

    [Fact]
    public async Task MultiCurrencyPost_ComputesFunctionalFromRate()
    {
        using var db = await SeedAsync();
        await PostEurCashSaleAsync(db, txn: 100m, rate: 1.10m);

        var cashLine = await db.JournalLines.IgnoreQueryFilters().SingleAsync(l => l.GlAccountId == CashId);
        cashLine.TxnAmount.Should().Be(100m);
        cashLine.FunctionalAmount.Should().Be(110m); // 100 × 1.10
        cashLine.FxRate.Should().Be(1.10m);
        cashLine.CurrencyId.Should().Be(EurId);
    }

    [Fact]
    public async Task Revalue_RateUp_PostsUnrealizedGain()
    {
        using var db = await SeedAsync();
        await PostEurCashSaleAsync(db, 100m, 1.10m); // booked functional 110

        var result = await Service(db).RevalueAsync(BookId, EurId, newRate: 1.15m, AsOf, postedByUserId: 7);

        result.NetForeignPosition.Should().Be(100m);
        result.Adjustment.Should().Be(5m); // 100×1.15 − 110
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == result.JournalEntryId);
        entry.Source.Should().Be(JournalSource.FX);
        entry.Lines.Single(l => l.GlAccountId == FxRevalId).Debit.Should().Be(5m);
        entry.Lines.Single(l => l.GlAccountId == FxGainId).Credit.Should().Be(5m);
        entry.AutoReverseNextPeriod.Should().BeTrue();
    }

    [Fact]
    public async Task Revalue_RateDown_PostsUnrealizedLoss()
    {
        using var db = await SeedAsync();
        await PostEurCashSaleAsync(db, 100m, 1.10m);

        var result = await Service(db).RevalueAsync(BookId, EurId, newRate: 1.05m, AsOf, 7);

        result.Adjustment.Should().Be(-5m); // 100×1.05 − 110
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == result.JournalEntryId);
        entry.Lines.Single(l => l.GlAccountId == FxLossId).Debit.Should().Be(5m);
        entry.Lines.Single(l => l.GlAccountId == FxRevalId).Credit.Should().Be(5m);
    }

    [Fact]
    public async Task Revalue_FunctionalCurrency_IsNoOp()
    {
        using var db = await SeedAsync();
        await PostEurCashSaleAsync(db, 100m, 1.10m);

        var result = await Service(db).RevalueAsync(BookId, UsdId, newRate: 1.20m, AsOf, 7); // functional = USD

        result.JournalEntryId.Should().BeNull();
        result.Adjustment.Should().Be(0m);
    }

    [Fact]
    public async Task Revalue_NoRateChange_NoEntry()
    {
        using var db = await SeedAsync();
        await PostEurCashSaleAsync(db, 100m, 1.10m);

        var result = await Service(db).RevalueAsync(BookId, EurId, newRate: 1.10m, AsOf, 7); // same rate

        result.Adjustment.Should().Be(0m);
        result.JournalEntryId.Should().BeNull();
    }
}
