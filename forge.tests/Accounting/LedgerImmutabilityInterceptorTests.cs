using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Coverage for the posted-ledger immutability interceptor (§2, §4, §5.2). The
/// interceptor is self-registered by <c>AppDbContext.OnConfiguring</c>, so the
/// InMemory test context exercises the real rules. Verifies that:
/// a Posted entry cannot be edited (except the Posted→Reversed carve-out) or
/// deleted, and a Posted entry's lines cannot be modified or deleted.
/// </summary>
public class LedgerImmutabilityInterceptorTests
{
    private const int BookId = 1;
    private const int UsdId = 1;

    private static async Task<(Data.Context.AppDbContext db, long entryId)> SeedPostedEntryAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "UTC", RoundingTolerance = 0.01m,
        });
        var fy = new FiscalYear { Id = 10, BookId = BookId, Name = "FY", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.Set<FiscalYear>().Add(fy);
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "P1",
            StartDate = new(2026, 1, 1), EndDate = new(2026, 1, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = 100, BookId = BookId, AccountNumber = "1000", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = 101, BookId = BookId, AccountNumber = "4000", Name = "Rev", AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });

        var entry = new JournalEntry
        {
            BookId = BookId, EntryNumber = 1, EntryDate = new(2026, 1, 15), FiscalPeriodId = 1000, FiscalYearId = 10,
            Source = JournalSource.Manual, CurrencyId = UsdId, Status = JournalEntryStatus.Posted, PostedBy = 1,
            Lines =
            [
                new JournalLine { BookId = BookId, LineNumber = 1, GlAccountId = 100, Debit = 100m, CurrencyId = UsdId, TxnAmount = 100m, FunctionalAmount = 100m, FxRate = 1m },
                new JournalLine { BookId = BookId, LineNumber = 2, GlAccountId = 101, Credit = 100m, CurrencyId = UsdId, TxnAmount = 100m, FunctionalAmount = 100m, FxRate = 1m },
            ],
        };
        db.Set<JournalEntry>().Add(entry);
        await db.SaveChangesAsync();
        return (db, entry.Id);
    }

    [Fact]
    public async Task ModifyingPostedEntryMemo_IsBlocked()
    {
        var (db, entryId) = await SeedPostedEntryAsync();
        var entry = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == entryId);

        entry.Memo = "tampered";

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutability violation*append-only*");
    }

    [Fact]
    public async Task DeletingPostedEntry_IsBlocked()
    {
        var (db, entryId) = await SeedPostedEntryAsync();
        // Detach everything seeded, then load the header WITHOUT its lines so EF
        // doesn't sever the required line→header relationship before SaveChanges
        // reaches the interceptor (the interceptor — and the Postgres FK/trigger
        // in production — is what actually blocks the delete).
        db.ChangeTracker.Clear();
        var entry = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == entryId);

        db.JournalEntries.Remove(entry);

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutability violation*cannot be deleted*");
    }

    [Fact]
    public async Task ModifyingPostedLine_IsBlocked()
    {
        var (db, entryId) = await SeedPostedEntryAsync();
        var line = await db.JournalLines.IgnoreQueryFilters()
            .Include(l => l.JournalEntry)
            .FirstAsync(l => l.JournalEntryId == entryId && l.Debit > 0);

        line.Debit = 999m;

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutability violation*journal line*");
    }

    [Fact]
    public async Task PostedToReversedFlip_IsAllowed()
    {
        var (db, entryId) = await SeedPostedEntryAsync();
        var entry = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == entryId);

        // The sole permitted mutation: Status flip + ReversedByEntryId link.
        entry.Status = JournalEntryStatus.Reversed;
        entry.ReversedByEntryId = 12345;

        var act = async () => await db.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }
}
