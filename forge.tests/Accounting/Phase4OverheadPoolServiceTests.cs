using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
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
/// Phase-4 standard costing — single-plant overhead pool + spending variance. Actual overhead accrues into
/// OVERHEAD_CONTROL; jobs absorb into OVERHEAD_APPLIED at close. The period close posts actual − applied to
/// OVERHEAD_SPENDING_VARIANCE (under-applied = unfavorable debit / over-applied = favorable credit) and clears
/// both balances to zero.
/// </summary>
public class Phase4OverheadPoolServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int ControlId = 134;   // OVERHEAD_CONTROL (asset/clearing)
    private const int AppliedId = 141;    // OVERHEAD_APPLIED (contra-expense)
    private const int SpendingId = 532;   // OVERHEAD_SPENDING_VARIANCE
    private const int AccruedId = 260;    // ACCRUED_EXPENSE

    private static readonly DateOnly AsOf = new(2026, 1, 31);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static OverheadPoolService Service(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn));

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
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ControlId, BookId = BookId, AccountNumber = "13400", Name = "Overhead Control", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AppliedId, BookId = BookId, AccountNumber = "51220", Name = "Overhead Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SpendingId, BookId = BookId, AccountNumber = "51320", Name = "Overhead Spending Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccruedId, BookId = BookId, AccountNumber = "26000", Name = "Accrued Expenses", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_CONTROL", GlAccountId = ControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_APPLIED", GlAccountId = AppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_SPENDING_VARIANCE", GlAccountId = SpendingId },
            new AccountDeterminationRule { BookId = BookId, Key = "ACCRUED_EXPENSE", GlAccountId = AccruedId });
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Builds an OVERHEAD_APPLIED credit balance (as the job-close absorption would), using a neutral
    /// accrued offset so the spending-variance account stays untouched until the close.</summary>
    private static Task SeedAppliedAsync(AppDbContext db, decimal applied) => Engine(db).PostAsync(new PostingRequest
    {
        BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Inventory, CurrencyId = UsdId,
        IdempotencyKey = $"applied:{applied}",
        Lines =
        [
            new PostingLine { AccountKey = "ACCRUED_EXPENSE", Debit = applied, Description = "seed applied offset" },
            new PostingLine { AccountKey = "OVERHEAD_APPLIED", Credit = applied, Description = "seed applied" },
        ],
    }, 7);

    private static async Task<decimal> BalanceAsync(AppDbContext db, int accountId, bool debitPositive) =>
        await (from line in db.JournalLines.IgnoreQueryFilters()
               join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
               where line.GlAccountId == accountId
                  && (je.Status == JournalEntryStatus.Posted || je.Status == JournalEntryStatus.Reversed)
               select debitPositive ? line.Debit - line.Credit : line.Credit - line.Debit).SumAsync();

    [Fact]
    public async Task Record_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        await Service(db, fullGlOn: false).RecordActualOverheadAsync(500m, "rent", AsOf, 7);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RecordActualOverhead_DebitsControl_CreditsAccrued()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(500m, "rent + utilities", AsOf, 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ControlId).Debit.Should().Be(500m);
        entry.Lines.Single(l => l.GlAccountId == AccruedId).Credit.Should().Be(500m);
    }

    [Fact]
    public async Task Close_UnderApplied_PostsUnfavorableSpendingVariance_AndClearsPool()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(1000m, "actual OH", AsOf, 7); // actual pool 1000
        await SeedAppliedAsync(db, 800m);                                          // applied 800

        var result = await Service(db).CloseOverheadPoolAsync(AsOf, 7);

        result.ActualOverhead.Should().Be(1000m);
        result.AppliedOverhead.Should().Be(800m);
        result.SpendingVariance.Should().Be(200m); // under-applied → unfavorable

        var close = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"Inventory:Overhead:Close:20260131");
        close.Lines.Single(l => l.GlAccountId == SpendingId).Debit.Should().Be(200m);

        // Pool + applied cleared to zero.
        (await BalanceAsync(db, ControlId, debitPositive: true)).Should().Be(0m);
        (await BalanceAsync(db, AppliedId, debitPositive: false)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_OverApplied_PostsFavorableSpendingVariance()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(700m, "actual OH", AsOf, 7); // actual 700
        await SeedAppliedAsync(db, 900m);                                         // applied 900

        var result = await Service(db).CloseOverheadPoolAsync(AsOf, 7);

        result.SpendingVariance.Should().Be(-200m); // over-applied → favorable
        var close = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.IdempotencyKey == $"Inventory:Overhead:Close:20260131");
        close.Lines.Single(l => l.GlAccountId == SpendingId).Credit.Should().Be(200m);
        (await BalanceAsync(db, ControlId, debitPositive: true)).Should().Be(0m);
    }

    [Fact]
    public async Task Close_CalledTwice_IsIdempotent()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(1000m, "OH", AsOf, 7);
        await SeedAppliedAsync(db, 800m);

        await Service(db).CloseOverheadPoolAsync(AsOf, 7);
        var second = await Service(db).CloseOverheadPoolAsync(AsOf, 7);

        second.Posted.Should().BeFalse("the close for this date already exists");
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.IdempotencyKey == $"Inventory:Overhead:Close:20260131")).Should().Be(1);
    }
}
