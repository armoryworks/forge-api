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

    private static FiscalPeriodCloseService Service(AppDbContext db) => new(db);

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
        var result = await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed);
        result.Status.Should().Be(FiscalPeriodStatus.SoftClosed);
        (await db.FiscalPeriods.SingleAsync(p => p.Id == PeriodId)).Status.Should().Be(FiscalPeriodStatus.SoftClosed);
    }

    [Fact]
    public async Task HardClose_FromSoftClosed()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task HardClose_DirectFromOpen()
    {
        using var db = await SeedAsync();
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed)).Status.Should().Be(FiscalPeriodStatus.HardClosed);
    }

    [Fact]
    public async Task Reopen_SoftClosedToOpen()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.SoftClosed);
        (await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open)).Status.Should().Be(FiscalPeriodStatus.Open);
    }

    [Fact]
    public async Task HardClosed_IsTerminal_ReopenThrows()
    {
        using var db = await SeedAsync(FiscalPeriodStatus.HardClosed);
        var act = async () => await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*from HardClosed*");
    }

    [Fact]
    public async Task SameStatus_Throws()
    {
        using var db = await SeedAsync();
        var act = async () => await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.Open);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already Open*");
    }

    [Fact]
    public async Task SoftClose_ThenPostWithoutOverride_Throws()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed);

        var act = async () => await Engine(db).PostAsync(SimpleEntry(), postedByUserId: 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_SOFT_CLOSED");
    }

    [Fact]
    public async Task SoftClose_ThenPostWithOverride_Succeeds()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.SoftClosed);

        var entry = await Engine(db).PostAsync(SimpleEntry(allowOverride: true), postedByUserId: 7);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task HardClose_ThenPost_Throws()
    {
        using var db = await SeedAsync();
        await Service(db).TransitionAsync(PeriodId, FiscalPeriodStatus.HardClosed);

        var act = async () => await Engine(db).PostAsync(SimpleEntry(allowOverride: true), postedByUserId: 7);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_HARD_CLOSED");
    }
}
