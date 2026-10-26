using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// The cached trial balance (<see cref="LedgerBalance"/>) must equal the sum of the Posted and Reversed journal
/// lines at its grain after every way the engine writes the ledger: a post, a foreign-currency post, a
/// maker-checker submit and approval, a reversal into another period, the reversal of a reversal, and postings
/// from concurrent requests that have no transaction of their own. Runs on a real Postgres so the period row
/// lock and the transactions are the production ones.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerBalanceCacheIntegrityTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int EurId = 2;
    private const int FiscalYearId = 10;
    private const int JanuaryId = 1000;
    private const int FebruaryId = 1001;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ExpenseId = 102;

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Set<Currency>().AddRange(
            new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" },
            new Currency { Id = EurId, Code = "EUR", Name = "Euro", Symbol = "€" });
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
        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod
            {
                Id = JanuaryId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = FiscalPeriodStatus.Open,
            },
            new FiscalPeriod
            {
                Id = FebruaryId, FiscalYearId = FiscalYearId, PeriodNumber = 2, Name = "Feb-Dec 2026",
                StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
            });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseId, BookId = BookId, AccountNumber = "60000", Name = "Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static PostingRequest Request(
        string key, DateOnly date, int debitAccount, int creditAccount, decimal amount,
        int currencyId = UsdId, decimal fxRate = 1m)
        => new()
        {
            BookId = BookId,
            EntryDate = date,
            Source = JournalSource.AR,
            SourceType = "Test",
            SourceId = 1,
            CurrencyId = currencyId,
            FxRate = fxRate,
            IdempotencyKey = key,
            Lines =
            [
                new PostingLine { GlAccountId = debitAccount, Debit = amount },
                new PostingLine { GlAccountId = creditAccount, Credit = amount },
            ],
        };

    private static async Task AssertCacheMatchesLinesAsync(AppDbContext db)
    {
        var fromLines = await (
                from line in db.JournalLines.IgnoreQueryFilters()
                join entry in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals entry.Id
                where entry.Status == JournalEntryStatus.Posted || entry.Status == JournalEntryStatus.Reversed
                group new { line.Debit, line.FunctionalAmount }
                    by new { line.BookId, line.GlAccountId, entry.FiscalPeriodId, line.CurrencyId } into g
                select new
                {
                    g.Key.BookId,
                    g.Key.GlAccountId,
                    g.Key.FiscalPeriodId,
                    g.Key.CurrencyId,
                    Debit = g.Sum(x => x.Debit > 0 ? x.FunctionalAmount : 0m),
                    Credit = g.Sum(x => x.Debit > 0 ? 0m : x.FunctionalAmount),
                })
            .ToListAsync();

        var cached = await db.LedgerBalances.AsNoTracking()
            .Select(b => new { b.BookId, b.GlAccountId, b.FiscalPeriodId, b.CurrencyId, Debit = b.DebitTotal, Credit = b.CreditTotal })
            .ToListAsync();

        cached.Should().BeEquivalentTo(fromLines);
    }

    [Fact]
    public async Task EveryEnginePath_KeepsTheCacheEqualToTheLines()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);
        var engine = Engine(db);

        var sale = await engine.PostAsync(Request("t:sale", new DateOnly(2026, 1, 10), CashId, RevenueId, 120m), postedByUserId: 7);
        await engine.PostAsync(Request("t:fx", new DateOnly(2026, 1, 12), CashId, RevenueId, 33.33m, EurId, 1.0837m), postedByUserId: 7);

        var pending = await engine.PostPendingAsync(Request("t:pending", new DateOnly(2026, 1, 15), ExpenseId, CashId, 45m), submittedByUserId: 7);
        await AssertCacheMatchesLinesAsync(db);
        await engine.ApprovePendingAsync(pending.Id, approvedByUserId: 8);

        var reversal = await engine.ReverseAsync(sale.Id, new DateOnly(2026, 2, 3), "wrong customer", reversedByUserId: 7);
        await engine.ReverseAsync(reversal.Id, new DateOnly(2026, 2, 4), "reinstate", reversedByUserId: 7);

        await AssertCacheMatchesLinesAsync(db);
        (await db.LedgerBalances.AsNoTracking().SingleAsync(b => b.GlAccountId == RevenueId && b.FiscalPeriodId == FebruaryId))
            .Should().BeEquivalentTo(new { DebitTotal = 120m, CreditTotal = 120m }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task ConcurrentPostingsWithoutACallerTransaction_LoseNoIncrement()
    {
        await using (var setup = fixture.CreateContext())
        {
            await ResetAsync(setup);
            await SeedAsync(setup);
            await Engine(setup).PostAsync(Request("t:seed", new DateOnly(2026, 1, 2), CashId, RevenueId, 1m), postedByUserId: 7);
        }

        const int postings = 16;
        await Task.WhenAll(Enumerable.Range(0, postings).Select(async i =>
        {
            await using var db = fixture.CreateContext();
            await Engine(db).PostAsync(
                Request($"t:concurrent:{i}", new DateOnly(2026, 1, 20), CashId, RevenueId, 10m), postedByUserId: 7);
        }));

        await using var check = fixture.CreateContext();
        await AssertCacheMatchesLinesAsync(check);
        (await check.LedgerBalances.AsNoTracking().SingleAsync(b => b.GlAccountId == CashId && b.FiscalPeriodId == JanuaryId))
            .DebitTotal.Should().Be(1m + postings * 10m);
    }

    [Fact]
    public async Task ATrackedBalanceRow_IsReReadBeforeItIsIncremented()
    {
        await using var first = fixture.CreateContext();
        await ResetAsync(first);
        await SeedAsync(first);
        var engine = Engine(first);
        await engine.PostAsync(Request("t:a", new DateOnly(2026, 1, 5), CashId, RevenueId, 5m), postedByUserId: 7);

        await using (var other = fixture.CreateContext())
            await Engine(other).PostAsync(Request("t:b", new DateOnly(2026, 1, 6), CashId, RevenueId, 7m), postedByUserId: 7);

        await engine.PostAsync(Request("t:c", new DateOnly(2026, 1, 7), CashId, RevenueId, 11m), postedByUserId: 7);

        await using var check = fixture.CreateContext();
        await AssertCacheMatchesLinesAsync(check);
        (await check.LedgerBalances.AsNoTracking().SingleAsync(b => b.GlAccountId == CashId && b.FiscalPeriodId == JanuaryId))
            .DebitTotal.Should().Be(23m);
    }
}
