using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5A ledger-register read endpoint: newest-first, paginated, filterable journal for a book, with
/// per-line account labels and drill-back refs. Read-only — data is hand-seeded (no posting engine)
/// since we're proving the projection/filter/pagination, not posting.
/// </summary>
public class GetLedgerRegisterHandlerTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ArId = 102;

    private static AppDbContext SeededDb()
    {
        var db = TestDbContextFactory.Create();

        db.GlAccounts.AddRange(
            Account(CashId, "1000", "Cash", AccountType.Asset, NormalBalance.Debit),
            Account(RevenueId, "4000", "Revenue", AccountType.Income, NormalBalance.Credit),
            Account(ArId, "1100", "Accounts Receivable", AccountType.Asset, NormalBalance.Debit));

        // Three posted-ish entries on ascending dates so newest-first ordering is observable.
        db.JournalEntries.AddRange(
            Entry(1001, 1, new DateOnly(2026, 1, 10), JournalEntryStatus.Posted, "First",
                Line(1, CashId, debit: 100m), Line(2, RevenueId, credit: 100m)),
            Entry(1002, 2, new DateOnly(2026, 2, 15), JournalEntryStatus.Posted, "Second",
                Line(3, ArId, debit: 50m), Line(4, RevenueId, credit: 50m)),
            Entry(1003, 3, new DateOnly(2026, 3, 20), JournalEntryStatus.PendingApproval, "Third",
                Line(5, CashId, debit: 25m), Line(6, RevenueId, credit: 25m)));

        db.SaveChanges();
        return db;
    }

    private static GlAccount Account(int id, string number, string name, AccountType type, NormalBalance normal) => new()
    {
        Id = id,
        BookId = BookId,
        AccountNumber = number,
        Name = name,
        AccountType = type,
        NormalBalance = normal,
        IsPostable = true,
        IsActive = true,
    };

    private static JournalEntry Entry(long id, long number, DateOnly date, JournalEntryStatus status, string memo,
        params JournalLine[] lines) => new()
    {
        Id = id,
        BookId = BookId,
        EntryNumber = number,
        EntryDate = date,
        FiscalPeriodId = PeriodId,
        FiscalYearId = FiscalYearId,
        Source = JournalSource.Manual,
        CurrencyId = UsdId,
        Status = status,
        Memo = memo,
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
    public async Task Returns_all_entries_newest_first_with_account_labels()
    {
        await using var db = SeededDb();
        var result = await new GetLedgerRegisterHandler(db).Handle(new GetLedgerRegisterQuery(BookId), default);

        result.TotalCount.Should().Be(3);
        result.TotalPages.Should().Be(1);
        result.Data.Select(e => e.EntryNumber).Should().ContainInOrder(3L, 2L, 1L); // newest date first

        var newest = result.Data[0];
        newest.EntryNumber.Should().Be(3);
        newest.Status.Should().Be(nameof(JournalEntryStatus.PendingApproval));
        newest.Lines.Should().HaveCount(2);
        newest.Lines[0].AccountNumber.Should().Be("1000");
        newest.Lines[0].AccountName.Should().Be("Cash");
        newest.Lines[0].Debit.Should().Be(25m);
    }

    [Fact]
    public async Task Status_filter_returns_only_matching_entries()
    {
        await using var db = SeededDb();
        var result = await new GetLedgerRegisterHandler(db)
            .Handle(new GetLedgerRegisterQuery(BookId, Status: JournalEntryStatus.Posted), default);

        result.TotalCount.Should().Be(2);
        result.Data.Should().OnlyContain(e => e.Status == nameof(JournalEntryStatus.Posted));
    }

    [Fact]
    public async Task Account_filter_returns_only_entries_touching_that_account()
    {
        await using var db = SeededDb();
        var result = await new GetLedgerRegisterHandler(db)
            .Handle(new GetLedgerRegisterQuery(BookId, GlAccountId: ArId), default);

        result.TotalCount.Should().Be(1);
        result.Data.Should().ContainSingle().Which.EntryNumber.Should().Be(2);
    }

    [Fact]
    public async Task Pagination_pages_and_reports_totals()
    {
        await using var db = SeededDb();
        var handler = new GetLedgerRegisterHandler(db);

        var page1 = await handler.Handle(new GetLedgerRegisterQuery(BookId, Page: 1, PageSize: 2), default);
        page1.Data.Should().HaveCount(2);
        page1.TotalCount.Should().Be(3);
        page1.TotalPages.Should().Be(2);
        page1.Data.Select(e => e.EntryNumber).Should().ContainInOrder(3L, 2L);

        var page2 = await handler.Handle(new GetLedgerRegisterQuery(BookId, Page: 2, PageSize: 2), default);
        page2.Data.Should().ContainSingle().Which.EntryNumber.Should().Be(1);
    }

    [Fact]
    public async Task PageSize_is_clamped_to_100()
    {
        await using var db = SeededDb();
        var result = await new GetLedgerRegisterHandler(db)
            .Handle(new GetLedgerRegisterQuery(BookId, PageSize: 5000), default);

        result.PageSize.Should().Be(100);
    }
}
