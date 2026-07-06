using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5A anomaly scan: deterministic reviewer flags over posted MANUAL entries — a hand-posting to a
/// control account, and a large manual entry at/above the caller's threshold. Automated (sub-ledger)
/// entries and normal small manual entries are not flagged.
/// </summary>
public class GetGlAnomaliesHandlerTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArControlId = 102;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();
        db.GlAccounts.AddRange(
            Account(CashId, "1000", control: false),
            Account(RevenueId, "4000", control: false),
            Account(ArControlId, "1100", control: true));

        db.JournalEntries.AddRange(
            Entry(1001, 1, JournalSource.Manual, Line(1, ArControlId, debit: 50m), Line(2, RevenueId, credit: 50m)),
            Entry(1002, 2, JournalSource.Manual, Line(3, CashId, debit: 5000m), Line(4, RevenueId, credit: 5000m)),
            Entry(1003, 3, JournalSource.Manual, Line(5, CashId, debit: 20m), Line(6, RevenueId, credit: 20m)),
            Entry(1004, 4, JournalSource.AR, Line(7, ArControlId, debit: 999999m), Line(8, RevenueId, credit: 999999m)));
        db.SaveChanges();
        return db;
    }

    private static GlAccount Account(int id, string number, bool control) => new()
    {
        Id = id,
        BookId = BookId,
        AccountNumber = number,
        Name = number,
        AccountType = AccountType.Asset,
        NormalBalance = NormalBalance.Debit,
        IsPostable = true,
        IsControlAccount = control,
        IsActive = true,
    };

    private static JournalEntry Entry(long id, long number, JournalSource source, params JournalLine[] lines) => new()
    {
        Id = id,
        BookId = BookId,
        EntryNumber = number,
        EntryDate = new DateOnly(2026, 1, (int)number),
        FiscalPeriodId = 1000,
        FiscalYearId = 10,
        Source = source,
        CurrencyId = UsdId,
        Status = JournalEntryStatus.Posted,
        Lines = lines,
    };

    private static JournalLine Line(long id, int accountId, decimal debit = 0m, decimal credit = 0m) => new()
    {
        Id = id,
        BookId = BookId,
        LineNumber = (int)id,
        GlAccountId = accountId,
        Debit = debit,
        Credit = credit,
        CurrencyId = UsdId,
        TxnAmount = debit > 0 ? debit : credit,
        FunctionalAmount = debit > 0 ? debit : credit,
        FxRate = 1m,
    };

    [Fact]
    public async Task Flags_manual_posting_to_a_control_account()
    {
        await using var db = SeededDb();
        var result = await new GetGlAnomaliesHandler(db)
            .Handle(new GetGlAnomaliesQuery(BookId, LargeManualThreshold: 1000m), default);

        var flagged = result.Single(a => a.EntryNumber == 1);
        flagged.Flags.Should().ContainSingle(f => f.Contains("control account"));
    }

    [Fact]
    public async Task Flags_large_manual_entries_at_or_above_the_threshold()
    {
        await using var db = SeededDb();
        var result = await new GetGlAnomaliesHandler(db)
            .Handle(new GetGlAnomaliesQuery(BookId, LargeManualThreshold: 1000m), default);

        var flagged = result.Single(a => a.EntryNumber == 2);
        flagged.Flags.Should().ContainSingle(f => f.Contains("Large manual entry"));
        flagged.TotalDebit.Should().Be(5000m);
    }

    [Fact]
    public async Task Ignores_normal_small_and_automated_entries()
    {
        await using var db = SeededDb();
        var result = await new GetGlAnomaliesHandler(db)
            .Handle(new GetGlAnomaliesQuery(BookId, LargeManualThreshold: 1000m), default);

        result.Should().NotContain(a => a.EntryNumber == 3); // normal small manual
        result.Should().NotContain(a => a.EntryNumber == 4); // automated (AR), even though it hits control + is huge
    }
}
