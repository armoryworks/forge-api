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
/// Real-Postgres proof of <see cref="ForgeGlPostingEngine.ReverseAsync"/>. Regression for the temp-key bug:
/// the Posted→Reversed link was assigned from <c>reversal.Id</c> BEFORE save — InMemory assigns keys at Add
/// so every unit test passed, but Npgsql's store-generated identity left it 0 and the
/// <c>fk_acct_journal_entries_reversed_by</c> FK rejected the flip (23503). The fix links via the navigation;
/// this test exercises the whole reverse path against the real provider (FKs + immutability trigger).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GlReversalAtomicityTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;
    private const int CashId = 100;
    private const int RevenueId = 101;

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
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "40000", Name = "Revenue", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reverse_OnRealPostgres_LinksOriginalToReversal()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db);
        var engine = Engine(db);

        var original = await engine.PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = new DateOnly(2026, 1, 15),
            Source = JournalSource.AR,
            SourceType = "Invoice",
            SourceId = 7,
            CurrencyId = UsdId,
            IdempotencyKey = "AR:Invoice:7:REVENUE",
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 120m },
                new PostingLine { GlAccountId = RevenueId, Credit = 120m },
            ],
        }, postedByUserId: 7);

        var reversal = await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "entered in error", 7);

        // Verify with a FRESH context — the link must be the REAL store-generated key, FK-satisfied.
        await using var verify = fixture.CreateContext();
        reversal.Id.Should().BeGreaterThan(0);
        var reloaded = await verify.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.Id == original.Id);
        reloaded.Status.Should().Be(JournalEntryStatus.Reversed);
        reloaded.ReversedByEntryId.Should().Be(reversal.Id);
        var reloadedReversal = await verify.JournalEntries.IgnoreQueryFilters()
            .Include(e => e.Lines).SingleAsync(e => e.Id == reversal.Id);
        reloadedReversal.ReversalOfEntryId.Should().Be(original.Id);
        reloadedReversal.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(120m);
        reloadedReversal.Lines.Single(l => l.GlAccountId == RevenueId).Debit.Should().Be(120m);
    }
}
