using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
/// Fire-test for the hand-written Postgres immutability triggers (defense-in-depth behind the C#
/// <c>LedgerImmutabilityInterceptor</c>). The triggers had been applied but never PROVOKED on a live
/// provider — this raw-SQL suite proves they actually reject UPDATE/DELETE on Posted journal entries and
/// their lines (ERRCODE <c>restrict_violation</c>), closing the "applied but unverified" go-live gap.
/// Raw SQL bypasses EF entirely, so the interceptor cannot mask a dead trigger.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LedgerImmutabilityTriggerAtomicityTests(PostgresFixture fixture)
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

    private static async Task<JournalEntry> SeedAndPostAsync(AppDbContext db)
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

        return await Engine(db).PostAsync(new PostingRequest
        {
            BookId = BookId,
            EntryDate = new DateOnly(2026, 1, 15),
            Source = JournalSource.AR,
            SourceType = "Invoice",
            SourceId = 1,
            CurrencyId = UsdId,
            IdempotencyKey = "AR:Invoice:1:REVENUE",
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        }, postedByUserId: 7);
    }

    [Fact]
    public async Task Trigger_RejectsRawUpdate_OnPostedEntry()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var entry = await SeedAndPostAsync(db);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "UPDATE acct_journal_entries SET memo = 'tampered' WHERE id = {0}", entry.Id);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("Ledger immutability violation");
    }

    [Fact]
    public async Task Trigger_RejectsRawDelete_OnPostedEntry()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var entry = await SeedAndPostAsync(db);

        var act = () => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM acct_journal_entries WHERE id = {0}", entry.Id);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("Ledger immutability violation");
    }

    [Fact]
    public async Task Trigger_RejectsRawUpdate_OnPostedLine()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var entry = await SeedAndPostAsync(db);
        var lineId = entry.Lines.First().Id;

        var act = () => db.Database.ExecuteSqlRawAsync(
            "UPDATE acct_journal_lines SET debit = 999 WHERE id = {0}", lineId);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("Ledger immutability violation");
    }

    [Fact]
    public async Task Trigger_RejectsRawDelete_OnPostedLine()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        var entry = await SeedAndPostAsync(db);
        var lineId = entry.Lines.First().Id;

        var act = () => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM acct_journal_lines WHERE id = {0}", lineId);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("Ledger immutability violation");
    }
}
