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
/// Phase-3 — recurring/standard journal templates. Proves a template posts a balanced entry through the
/// engine for a given date, re-posting the same template+date is idempotent, and an auto-reverse template
/// stamps the flag.
/// </summary>
public class Phase3JournalTemplateServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int RentId = 101;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static JournalTemplateService Service(AppDbContext db) => new(db, Engine(db));

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
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RentId, BookId = BookId, AccountNumber = "61000", Name = "Rent Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "RENT", GlAccountId = RentId });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreateJournalTemplateModel RentTemplate(bool autoReverse = false) => new(
        BookId, "Monthly rent", "Standard monthly rent accrual", "Rent", autoReverse,
        [
            new JournalTemplateLineModel("RENT", null, 1200m, 0m, "Rent expense", null, null),
            new JournalTemplateLineModel("CASH", null, 0m, 1200m, "Cash", null, null),
        ]);

    [Fact]
    public async Task Create_ThenList_RoundTrips()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var created = await svc.CreateAsync(RentTemplate());

        created.Lines.Should().HaveCount(2);
        var list = await svc.ListAsync(BookId);
        list.Should().ContainSingle(t => t.Id == created.Id && t.Name == "Monthly rent");
    }

    [Fact]
    public async Task PostFromTemplate_PostsBalancedEntry()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var tpl = await svc.CreateAsync(RentTemplate());

        var posted = await svc.PostFromTemplateAsync(tpl.Id, new DateOnly(2026, 3, 1), memoOverride: null, postedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.SourceType.Should().Be("JournalTemplate");
        entry.SourceId.Should().Be(tpl.Id);
        entry.Lines.Single(l => l.GlAccountId == RentId).Debit.Should().Be(1200m);
        entry.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(1200m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PostFromTemplate_SameDate_IsIdempotent()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var tpl = await svc.CreateAsync(RentTemplate());

        var first = await svc.PostFromTemplateAsync(tpl.Id, new DateOnly(2026, 3, 1), null, 7);
        var second = await svc.PostFromTemplateAsync(tpl.Id, new DateOnly(2026, 3, 1), null, 7);

        second.JournalEntryId.Should().Be(first.JournalEntryId);
        (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.SourceType == "JournalTemplate")).Should().Be(1);
    }

    [Fact]
    public async Task PostFromTemplate_AutoReverse_StampsFlag()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var tpl = await svc.CreateAsync(RentTemplate(autoReverse: true));

        var posted = await svc.PostFromTemplateAsync(tpl.Id, new DateOnly(2026, 3, 1), null, 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.AutoReverseNextPeriod.Should().BeTrue();
    }
}
