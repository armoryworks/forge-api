using System.Text;

using FluentAssertions;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// QB-001 CPA CSV exports (Part A): content of the three files — header rows,
/// RFC 4180 quoting (memo with embedded comma + quote), invariant 2dp amounts,
/// ISO dates, date-range filtering, Reversed entries included, Draft excluded,
/// journal-summary one-sided netting + zero-net omission, and the
/// month-shaped filename.
/// </summary>
public class GlCsvExportTests
{
    private const int BookId = 1;
    private const int CashId = 100;
    private const int RevenueId = 101;
    private const int ClearingId = 102;

    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    private static AppDbContext Seed()
    {
        var db = TestDbContextFactory.Create();

        db.GlAccounts.AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash" },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue" },
            new GlAccount { Id = ClearingId, BookId = BookId, AccountNumber = "2090", Name = "Clearing" });

        // E1 — Posted, in range; memo exercises the RFC 4180 quoting edge.
        AddEntry(db, entryId: 1, entryNumber: 1, new DateOnly(2026, 6, 5), JournalEntryStatus.Posted,
            memo: "He said \"hi\", twice", debitAccountId: CashId, creditAccountId: RevenueId, amount: 100m,
            jobId: 7, costCenterId: 3);

        // E2 (Reversed original) + E3 (its Posted reversal) — both must appear in
        // gl-detail; they net to zero on the Clearing pair in the summary.
        AddEntry(db, entryId: 2, entryNumber: 2, new DateOnly(2026, 6, 10), JournalEntryStatus.Reversed,
            memo: "original", debitAccountId: ClearingId, creditAccountId: CashId, amount: 50m);
        AddEntry(db, entryId: 3, entryNumber: 3, new DateOnly(2026, 6, 11), JournalEntryStatus.Posted,
            memo: "reversal", debitAccountId: CashId, creditAccountId: ClearingId, amount: 50m);

        // E4 — Posted but OUTSIDE the range; must be filtered out.
        AddEntry(db, entryId: 4, entryNumber: 4, new DateOnly(2026, 7, 2), JournalEntryStatus.Posted,
            memo: "july", debitAccountId: CashId, creditAccountId: RevenueId, amount: 999m);

        // E5 — Draft; never exported.
        AddEntry(db, entryId: 5, entryNumber: 5, new DateOnly(2026, 6, 15), JournalEntryStatus.Draft,
            memo: "draft", debitAccountId: CashId, creditAccountId: RevenueId, amount: 777m);

        db.SaveChanges();
        return db;
    }

    private static void AddEntry(
        AppDbContext db, long entryId, long entryNumber, DateOnly date, JournalEntryStatus status,
        string memo, int debitAccountId, int creditAccountId, decimal amount,
        int? jobId = null, int? costCenterId = null)
    {
        db.JournalEntries.Add(new JournalEntry
        {
            Id = entryId, BookId = BookId, EntryNumber = entryNumber, EntryDate = date,
            FiscalPeriodId = 1, FiscalYearId = 1, CurrencyId = 1,
            Source = JournalSource.Manual, Memo = memo, Status = status,
        });
        db.JournalLines.AddRange(
            new JournalLine
            {
                Id = entryId * 10 + 1, JournalEntryId = entryId, BookId = BookId, LineNumber = 1,
                GlAccountId = debitAccountId, Debit = amount, Credit = 0m, CurrencyId = 1,
                TxnAmount = amount, FunctionalAmount = amount, FxRate = 1m,
                JobId = jobId, CostCenterId = costCenterId,
            },
            new JournalLine
            {
                Id = entryId * 10 + 2, JournalEntryId = entryId, BookId = BookId, LineNumber = 2,
                GlAccountId = creditAccountId, Debit = 0m, Credit = amount, CurrencyId = 1,
                TxnAmount = amount, FunctionalAmount = amount, FxRate = 1m,
            });
    }

    private static string[] Lines(byte[] content)
        => Encoding.UTF8.GetString(content).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    // ── trial-balance.csv ──

    [Fact]
    public async Task TrialBalanceCsv_HasHeader_FormattedAmounts_AndMonthFileName()
    {
        await using var db = Seed();
        var handler = new ExportTrialBalanceCsvHandler(new TrialBalanceService(db));

        var result = await handler.Handle(new ExportTrialBalanceCsvQuery(BookId, From, To), CancellationToken.None);

        result.FileName.Should().Be("trial-balance-2026-06.csv");

        var lines = Lines(result.Content);
        lines[0].Should().Be("accountNumber,accountName,debit,credit,net");
        // Cash: Dr 100 (E1) + Dr 50 (E3) − Cr 50 (E2) → debit 150.00, credit 50.00, net 100.00
        lines.Should().Contain("1000,Cash,150.00,50.00,100.00");
        // Revenue: only E1's credit — E4 (July) and E5 (Draft) excluded
        lines.Should().Contain("4000,Revenue,0.00,100.00,-100.00");
        // Clearing: E2 Dr 50 + E3 Cr 50 → nets to zero but stays on the trial balance
        lines.Should().Contain("2090,Clearing,50.00,50.00,0.00");
    }

    [Fact]
    public async Task TrialBalanceCsv_NoRange_UsesAllSuffix()
    {
        await using var db = Seed();
        var handler = new ExportTrialBalanceCsvHandler(new TrialBalanceService(db));

        var result = await handler.Handle(new ExportTrialBalanceCsvQuery(BookId), CancellationToken.None);

        result.FileName.Should().Be("trial-balance-all.csv");
    }

    // ── gl-detail.csv ──

    [Fact]
    public async Task GlDetailCsv_OneRowPerLine_OrderedAndRangeFiltered_ReversedIncluded()
    {
        await using var db = Seed();
        var handler = new ExportGlDetailCsvHandler(db);

        var result = await handler.Handle(new ExportGlDetailCsvQuery(BookId, From, To), CancellationToken.None);

        result.FileName.Should().Be("gl-detail-2026-06.csv");

        var lines = Lines(result.Content);
        lines[0].Should().Be(
            "entryNumber,entryDate,source,sourceRef,memo,accountNumber,accountName,debit,credit,jobId,costCenterId");

        // E1 (2 lines) + E2 Reversed (2) + E3 (2) = 6 data rows; E4 out of range, E5 Draft.
        lines.Should().HaveCount(7);

        // Quoting edge: memo with comma + embedded (doubled) quotes; ISO date; dimensions.
        lines[1].Should().Be("1,2026-06-05,Manual,,\"He said \"\"hi\"\", twice\",1000,Cash,100.00,0.00,7,3");
        lines[2].Should().Be("1,2026-06-05,Manual,,\"He said \"\"hi\"\", twice\",4000,Revenue,0.00,100.00,,");

        // Ordered by entry number then line number; the Reversed original is present.
        lines[3].Should().StartWith("2,2026-06-10,Manual,,original,2090,Clearing,50.00,0.00");
        lines[4].Should().StartWith("2,2026-06-10,Manual,,original,1000,Cash,0.00,50.00");
        lines[5].Should().StartWith("3,2026-06-11,Manual,,reversal,1000,Cash,50.00,0.00");
        lines[6].Should().StartWith("3,2026-06-11,Manual,,reversal,2090,Clearing,0.00,50.00");
    }

    [Fact]
    public async Task GlDetailCsv_EmitsSourceRef_WhenSourceLinkPresent()
    {
        await using var db = TestDbContextFactory.Create();
        db.GlAccounts.Add(new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash" });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 1, BookId = BookId, EntryNumber = 1, EntryDate = From,
            FiscalPeriodId = 1, FiscalYearId = 1, CurrencyId = 1,
            Source = JournalSource.AR, SourceType = "Invoice", SourceId = 42,
            Status = JournalEntryStatus.Posted,
        });
        db.JournalLines.Add(new JournalLine
        {
            Id = 1, JournalEntryId = 1, BookId = BookId, LineNumber = 1,
            GlAccountId = CashId, Debit = 10m, Credit = 0m, CurrencyId = 1,
            TxnAmount = 10m, FunctionalAmount = 10m, FxRate = 1m,
        });
        await db.SaveChangesAsync();

        var handler = new ExportGlDetailCsvHandler(db);
        var result = await handler.Handle(new ExportGlDetailCsvQuery(BookId, From, To), CancellationToken.None);

        Lines(result.Content)[1].Should().Be("1,2026-06-01,AR,Invoice:42,,1000,Cash,10.00,0.00,,");
    }

    // ── journal-summary.csv ──

    [Fact]
    public async Task JournalSummaryCsv_OneSidedNets_OmitsZeroNetAccounts_AndBalances()
    {
        await using var db = Seed();
        var handler = new ExportJournalSummaryCsvHandler(new TrialBalanceService(db));

        var result = await handler.Handle(new ExportJournalSummaryCsvQuery(BookId, From, To), CancellationToken.None);

        result.FileName.Should().Be("journal-summary-2026-06.csv");

        var lines = Lines(result.Content);
        lines[0].Should().Be("accountNumber,accountName,totalDebit,totalCredit");

        // The one-JE shape: Cash net-debit 100, Revenue net-credit 100; the
        // zero-net Clearing account is omitted entirely.
        lines.Should().HaveCount(3);
        lines[1].Should().Be("1000,Cash,100.00,0.00");
        lines[2].Should().Be("4000,Revenue,0.00,100.00");
    }
}
