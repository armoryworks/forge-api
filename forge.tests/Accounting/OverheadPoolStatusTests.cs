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
/// The overhead pool status read and the record-actual source account: status shows actual, applied and the
/// variance a close would post, flips to closed after the close, and reads empty with full GL off; recording
/// with a source account credits that account instead of accrued expenses.
/// </summary>
public class OverheadPoolStatusTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int ControlId = 134;
    private const int AppliedId = 141;
    private const int SpendingId = 532;
    private const int AccruedId = 260;
    private const int UtilitiesId = 640;
    private const int RevenueId = 400;

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
        db.Set<FiscalYear>().Add(new FiscalYear { Id = 10, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = ControlId, BookId = BookId, AccountNumber = "13400", Name = "Overhead Control", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AppliedId, BookId = BookId, AccountNumber = "51220", Name = "Overhead Absorbed", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = SpendingId, BookId = BookId, AccountNumber = "51320", Name = "Overhead Spending Variance", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = AccruedId, BookId = BookId, AccountNumber = "26000", Name = "Accrued Expenses", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = UtilitiesId, BookId = BookId, AccountNumber = "64000", Name = "Utilities", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Sales", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_CONTROL", GlAccountId = ControlId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_APPLIED", GlAccountId = AppliedId },
            new AccountDeterminationRule { BookId = BookId, Key = "OVERHEAD_SPENDING_VARIANCE", GlAccountId = SpendingId },
            new AccountDeterminationRule { BookId = BookId, Key = "ACCRUED_EXPENSE", GlAccountId = AccruedId });
        await db.SaveChangesAsync();
        return db;
    }

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

    [Fact]
    public async Task Status_shows_actual_applied_and_the_pending_variance()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(1000m, "rent", AsOf, 7);
        await SeedAppliedAsync(db, 800m);

        var status = await Service(db).GetStatusAsync(AsOf);

        status.IsConfigured.Should().BeTrue();
        status.ActualOverhead.Should().Be(1000m);
        status.AppliedOverhead.Should().Be(800m);
        status.Variance.Should().Be(200m);
        status.ClosedOnDate.Should().BeFalse();
        status.LastClosedOn.Should().BeNull();
    }

    [Fact]
    public async Task Status_after_the_close_is_closed_and_cleared()
    {
        using var db = await SeedAsync();
        await Service(db).RecordActualOverheadAsync(1000m, "rent", AsOf, 7);
        await SeedAppliedAsync(db, 800m);
        await Service(db).CloseOverheadPoolAsync(AsOf, 7);

        var status = await Service(db).GetStatusAsync(AsOf);
        var nextMonth = await Service(db).GetStatusAsync(new DateOnly(2026, 2, 28));

        status.ClosedOnDate.Should().BeTrue();
        status.ActualOverhead.Should().Be(0m);
        status.AppliedOverhead.Should().Be(0m);
        status.LastClosedOn.Should().Be(AsOf);
        nextMonth.ClosedOnDate.Should().BeFalse();
        nextMonth.LastClosedOn.Should().Be(AsOf);
    }

    [Fact]
    public async Task Status_with_full_gl_off_is_empty()
    {
        using var db = await SeedAsync();

        var status = await Service(db, fullGlOn: false).GetStatusAsync(AsOf);

        status.IsConfigured.Should().BeFalse();
        status.ActualOverhead.Should().Be(0m);
    }

    [Fact]
    public async Task Record_with_a_source_account_credits_that_account()
    {
        using var db = await SeedAsync();

        await Service(db).RecordActualOverheadAsync(450m, "utilities", AsOf, 7, UtilitiesId);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == ControlId).Debit.Should().Be(450m);
        entry.Lines.Single(l => l.GlAccountId == UtilitiesId).Credit.Should().Be(450m);
        entry.Lines.Should().NotContain(l => l.GlAccountId == AccruedId);
    }

    [Fact]
    public async Task Record_refuses_a_source_account_of_the_wrong_type()
    {
        using var db = await SeedAsync();

        var act = () => Service(db).RecordActualOverheadAsync(450m, "sales", AsOf, 7, RevenueId);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }
}
