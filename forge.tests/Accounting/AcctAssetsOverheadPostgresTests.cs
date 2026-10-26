using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// On real Postgres: the depreciation preview for a month equals what the run then posts to the ledger (per
/// asset and in total), and recording actual overhead then closing the pool posts the spending variance exactly
/// once however often the close is repeated.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AcctAssetsOverheadPostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;

    private static readonly (string Key, int Id, string Number, AccountType Type)[] Accounts =
    [
        ("EQUIPMENT", 150, "15000", AccountType.Asset),
        ("ACCUM_DEP", 151, "15900", AccountType.Asset),
        ("OVERHEAD_CONTROL", 134, "13400", AccountType.Asset),
        ("ACCRUED_EXPENSE", 260, "26000", AccountType.Liability),
        ("OVERHEAD_APPLIED", 141, "51220", AccountType.Expense),
        ("OVERHEAD_SPENDING_VARIANCE", 532, "51320", AccountType.Expense),
        ("DEP_EXPENSE", 600, "62000", AccountType.Expense),
    ];

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

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
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, IsBaseCurrency = true, IsActive = true });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(2026, month, 1);
            db.Set<FiscalPeriod>().Add(new FiscalPeriod
            {
                Id = 1000 + month, FiscalYearId = 10, PeriodNumber = month, Name = $"P{month}",
                StartDate = start, EndDate = start.AddMonths(1).AddDays(-1), Status = FiscalPeriodStatus.Open,
            });
        }
        foreach (var (key, id, number, type) in Accounts)
        {
            db.Set<GlAccount>().Add(new GlAccount
            {
                Id = id, BookId = BookId, AccountNumber = number, Name = key, AccountType = type,
                NormalBalance = type is AccountType.Asset or AccountType.Expense ? NormalBalance.Debit : NormalBalance.Credit,
                IsPostable = true, IsActive = true,
            });
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = key, GlAccountId = id });
        }
        await db.SaveChangesAsync();
        await AdvanceIdentitiesAsync(db);
    }

    private static Task AdvanceIdentitiesAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT c.table_name, c.column_name,
                   pg_get_serial_sequence(quote_ident(c.table_name), c.column_name) AS seq
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.table_name IN ('currencies', 'acct_books', 'acct_fiscal_years', 'acct_fiscal_periods', 'acct_gl_accounts')
              AND pg_get_serial_sequence(quote_ident(c.table_name), c.column_name) IS NOT NULL) LOOP
    EXECUTE format('SELECT setval(%L, (SELECT COALESCE(MAX(%I), 0) + 1 FROM %I), false)', r.seq, r.column_name, r.table_name);
  END LOOP;
END $$;");

    private static CreateFixedAssetModel Asset(string name, decimal cost, int life, DepreciationMethod method) => new(
        BookId, name, null, cost, 0m, new DateOnly(2026, 1, 1), life, 150, 151, 600, method);

    [Fact]
    public async Task Depreciation_preview_equals_the_posted_run()
    {
        await using (var setup = fixture.CreateContext())
        {
            await ResetAsync(setup);
            await SeedAsync(setup);
            var svc = new DepreciationService(setup, Engine(setup));
            await svc.CreateAssetAsync(Asset("Lathe", 3600m, 36, DepreciationMethod.StraightLine));
            await svc.CreateAssetAsync(Asset("Press", 10000m, 60, DepreciationMethod.DecliningBalance));
            await svc.RunDepreciationAsync(BookId, new DateOnly(2026, 1, 1), 7);
        }

        var month = new DateOnly(2026, 2, 1);
        DepreciationPreviewResult preview;
        await using (var db = fixture.CreateContext())
            preview = await new DepreciationService(db, Engine(db)).PreviewDepreciationAsync(BookId, month);

        await using (var db = fixture.CreateContext())
        {
            (await db.JournalEntries.IgnoreQueryFilters().CountAsync(e => e.EntryDate == month)).Should().Be(0);

            await using var tx = await db.Database.BeginTransactionAsync();
            var run = await new DepreciationService(db, Engine(db)).RunDepreciationAsync(BookId, month, 7);
            await tx.CommitAsync();

            run.TotalAmount.Should().Be(preview.TotalAmount);
            run.AssetsDepreciated.Should().Be(2);
        }

        await using (var db = fixture.CreateContext())
        {
            var posted = await (from line in db.JournalLines.IgnoreQueryFilters()
                                join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
                                where je.EntryDate == month && je.SourceType == "Depreciation" && line.GlAccountId == 600
                                select new { je.SourceId, line.Debit }).ToListAsync();
            posted.Select(p => (p.SourceId, p.Debit)).Should()
                .BeEquivalentTo(preview.Lines.Select(l => ((int?)l.FixedAssetId, l.Amount)));
            posted.Sum(p => p.Debit).Should().Be(preview.TotalAmount);
        }
    }

    [Fact]
    public async Task Overhead_record_then_close_posts_the_variance_once()
    {
        var asOf = new DateOnly(2026, 3, 31);
        await using (var setup = fixture.CreateContext())
        {
            await ResetAsync(setup);
            await SeedAsync(setup);
            var pool = new OverheadPoolService(setup, Engine(setup), new FullGlOn());
            await pool.RecordActualOverheadAsync(1250m, "rent", new DateOnly(2026, 3, 5), 7);
            await pool.RecordActualOverheadAsync(250m, "utilities", new DateOnly(2026, 3, 20), 7);
            await Engine(setup).PostAsync(new PostingRequest
            {
                BookId = BookId, EntryDate = new DateOnly(2026, 3, 25), Source = JournalSource.Inventory, CurrencyId = UsdId,
                IdempotencyKey = "absorb:march",
                Lines =
                [
                    new PostingLine { AccountKey = "ACCRUED_EXPENSE", Debit = 1100m, Description = "absorb offset" },
                    new PostingLine { AccountKey = "OVERHEAD_APPLIED", Credit = 1100m, Description = "absorbed" },
                ],
            }, 7);
        }

        OverheadPoolStatusModel before;
        await using (var db = fixture.CreateContext())
            before = await new OverheadPoolService(db, Engine(db), new FullGlOn()).GetStatusAsync(asOf);
        before.Variance.Should().Be(400m);

        OverheadPoolCloseResult first;
        OverheadPoolCloseResult second;
        await using (var db = fixture.CreateContext())
            first = await new OverheadPoolService(db, Engine(db), new FullGlOn()).CloseOverheadPoolAsync(asOf, 7);
        await using (var db = fixture.CreateContext())
            second = await new OverheadPoolService(db, Engine(db), new FullGlOn()).CloseOverheadPoolAsync(asOf, 7);

        first.Posted.Should().BeTrue();
        first.SpendingVariance.Should().Be(before.Variance);
        second.Posted.Should().BeFalse();

        await using (var db = fixture.CreateContext())
        {
            var varianceDebits = await (from line in db.JournalLines.IgnoreQueryFilters()
                                        join je in db.JournalEntries.IgnoreQueryFilters() on line.JournalEntryId equals je.Id
                                        where line.GlAccountId == 532
                                        select line.Debit - line.Credit).ToListAsync();
            varianceDebits.Should().ContainSingle().Which.Should().Be(400m);

            var after = await new OverheadPoolService(db, Engine(db), new FullGlOn()).GetStatusAsync(asOf);
            after.ClosedOnDate.Should().BeTrue();
            after.ActualOverhead.Should().Be(0m);
            after.AppliedOverhead.Should().Be(0m);
        }
    }
}
