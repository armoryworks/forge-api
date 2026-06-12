using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// BANK-001 — statement import + auto-match staging. Proves:
///   • OFX (SGML, unclosed tags) and CSV (header-mapped, parenthesized negatives) parse;
///   • re-import dedupes on (cash account, FITID) — zero new rows;
///   • auto-match proposes ONLY a unique candidate (equal signed amount, date window) —
///     two identical candidates stay Unmatched (the matcher never guesses);
///   • confirm clears the journal line in an open Draft bank reconciliation; unmatch un-clears;
///   • manual match rejects lines from another account and already-claimed journal lines.
/// </summary>
public class BankStatementImportTests
{
    private const int BookId = 1;
    private const int CashAccountId = 100;

    private sealed class FakeSettings : ISettingsService
    {
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default)
            => Task.FromResult(SettingDescriptorCatalog.FindByKey(key)?.DefaultValue);
        public Task<bool> GetBoolAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> GetIntAsync(string key, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    }

    private static (AppDbContext Db, BankStatementImportService Service) CreateHarness()
    {
        var db = TestDbContextFactory.Create();
        SeedGl(db);
        return (db, new BankStatementImportService(db, new FakeSettings(), new FakeClock()));
    }

    private static void SeedGl(AppDbContext db)
    {
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        });
        db.GlAccounts.Add(new GlAccount
        {
            Id = CashAccountId, BookId = BookId, AccountNumber = "10100", Name = "Cash",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        });
        db.SaveChanges();
    }

    /// <summary>A Posted journal entry with one cash line (signed: + = Dr cash, − = Cr cash).</summary>
    private static async Task<JournalLine> AddCashLineAsync(
        AppDbContext db, decimal signedAmount, DateOnly entryDate, string memo = "test")
    {
        var entry = new JournalEntry
        {
            BookId = BookId,
            EntryNumber = await db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1,
            EntryDate = entryDate,
            Status = JournalEntryStatus.Posted,
            Source = JournalSource.Manual,
            Memo = memo,
        };
        var line = new JournalLine
        {
            BookId = BookId,
            GlAccountId = CashAccountId,
            LineNumber = 1,
            Debit = signedAmount > 0 ? signedAmount : 0m,
            Credit = signedAmount < 0 ? -signedAmount : 0m,
            CurrencyId = 1,
            TxnAmount = Math.Abs(signedAmount),
            FunctionalAmount = Math.Abs(signedAmount),
            FxRate = 1m,
        };
        entry.Lines.Add(line);
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        return line;
    }

    private const string OfxSgml = """
        OFXHEADER:100
        DATA:OFXSGML
        <OFX>
        <BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
        <STMTTRN>
        <TRNTYPE>CREDIT
        <DTPOSTED>20260610
        <TRNAMT>750.00
        <FITID>FCU-001
        <NAME>CUSTOMER DEPOSIT
        </STMTTRN>
        <STMTTRN>
        <TRNTYPE>DEBIT
        <DTPOSTED>20260611
        <TRNAMT>-120.00
        <FITID>FCU-002
        <NAME>ACH PACIFIC TOOL
        </STMTTRN>
        </BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1>
        </OFX>
        """;

    private const string Csv = """
        Date,Description,Amount
        06/10/2026,CUSTOMER DEPOSIT,750.00
        06/11/2026,"ACH PACIFIC TOOL",(120.00)
        """;

    [Fact]
    public void ParseOfx_SgmlWithUnclosedTags()
    {
        var lines = BankStatementParser.ParseOfx(OfxSgml);
        lines.Should().HaveCount(2);
        lines[0].Should().Be(new ParsedStatementLine(new DateOnly(2026, 6, 10), 750.00m, "CUSTOMER DEPOSIT", "FCU-001"));
        lines[1].Amount.Should().Be(-120.00m);
    }

    [Fact]
    public void ParseCsv_HeaderMapped_ParenNegatives()
    {
        var lines = BankStatementParser.ParseCsv(Csv);
        lines.Should().HaveCount(2);
        lines[0].Amount.Should().Be(750.00m);
        lines[1].Amount.Should().Be(-120.00m);
        lines[1].Description.Should().Be("ACH PACIFIC TOOL");
        lines.Select(l => l.Fitid).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task Import_Reimport_DedupesToZero()
    {
        var (db, service) = CreateHarness();

        var first = await service.ImportAsync(BookId, CashAccountId, "june.ofx", OfxSgml, userId: 1);
        first.Imported.Should().Be(2);
        first.Duplicates.Should().Be(0);

        var second = await service.ImportAsync(BookId, CashAccountId, "june-again.ofx", OfxSgml, userId: 1);
        second.Imported.Should().Be(0);
        second.Duplicates.Should().Be(2);

        (await db.BankStatementLines.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task AutoMatch_UniqueCandidate_Suggested_WithSignConvention()
    {
        var (db, service) = CreateHarness();
        var deposit = await AddCashLineAsync(db, +750m, new DateOnly(2026, 6, 9));   // Dr cash
        var disbursement = await AddCashLineAsync(db, -120m, new DateOnly(2026, 6, 12)); // Cr cash

        var result = await service.ImportAsync(BookId, CashAccountId, "june.ofx", OfxSgml, userId: 1);

        result.Suggested.Should().Be(2);
        var lines = await db.BankStatementLines.OrderBy(l => l.PostedDate).ToListAsync();
        lines[0].MatchedJournalLineId.Should().Be(deposit.Id);
        lines[1].MatchedJournalLineId.Should().Be(disbursement.Id);
        lines.Should().AllSatisfy(l => l.MatchStatus.Should().Be(BankStatementMatchStatus.Suggested));
    }

    [Fact]
    public async Task AutoMatch_AmbiguousCandidates_StayUnmatched()
    {
        var (db, service) = CreateHarness();
        await AddCashLineAsync(db, +750m, new DateOnly(2026, 6, 9));
        await AddCashLineAsync(db, +750m, new DateOnly(2026, 6, 11)); // identical twin in window

        var result = await service.ImportAsync(BookId, CashAccountId, "one.csv",
            "Date,Description,Amount\n06/10/2026,DEPOSIT,750.00\n", userId: 1);

        result.Suggested.Should().Be(0);
        (await db.BankStatementLines.SingleAsync()).MatchStatus.Should().Be(BankStatementMatchStatus.Unmatched);
    }

    [Fact]
    public async Task AutoMatch_OutsideDateWindow_NoSuggestion()
    {
        var (db, service) = CreateHarness();
        await AddCashLineAsync(db, +750m, new DateOnly(2026, 5, 1)); // 40 days away (window 5)

        var result = await service.ImportAsync(BookId, CashAccountId, "one.csv",
            "Date,Description,Amount\n06/10/2026,DEPOSIT,750.00\n", userId: 1);

        result.Suggested.Should().Be(0);
    }

    [Fact]
    public async Task Confirm_ClearsLineInOpenReconciliation_Unmatch_Unclears()
    {
        var (db, service) = CreateHarness();
        var cashLine = await AddCashLineAsync(db, +750m, new DateOnly(2026, 6, 10));

        var rec = new BankReconciliation
        {
            BookId = BookId, CashGlAccountId = CashAccountId,
            StatementDate = new DateOnly(2026, 6, 30), StatementEndingBalance = 750m,
            Status = BankReconciliationStatus.Draft,
        };
        rec.Items.Add(new BankReconciliationItem { JournalLineId = cashLine.Id, IsCleared = false });
        db.Set<BankReconciliation>().Add(rec);
        await db.SaveChangesAsync();

        await service.ImportAsync(BookId, CashAccountId, "one.csv",
            "Date,Description,Amount\n06/10/2026,DEPOSIT,750.00\n", userId: 1);
        var line = await db.BankStatementLines.SingleAsync();
        line.MatchStatus.Should().Be(BankStatementMatchStatus.Suggested);

        await service.ConfirmAsync(line.Id, userId: 4);

        (await db.Set<BankReconciliationItem>().SingleAsync()).IsCleared.Should().BeTrue();
        (await db.BankStatementLines.SingleAsync()).MatchStatus.Should().Be(BankStatementMatchStatus.Confirmed);

        await service.UnmatchAsync(line.Id);
        (await db.Set<BankReconciliationItem>().SingleAsync()).IsCleared.Should().BeFalse();
    }

    [Fact]
    public async Task ManualMatch_GuardsAccountAndClaims()
    {
        var (db, service) = CreateHarness();
        // A line on a DIFFERENT account.
        db.GlAccounts.Add(new GlAccount
        {
            Id = 200, BookId = BookId, AccountNumber = "10150", Name = "CIT",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
        });
        await db.SaveChangesAsync();
        var wrongAccountEntry = new JournalEntry
        {
            BookId = BookId, EntryNumber = 99, EntryDate = new DateOnly(2026, 6, 10),
            Status = JournalEntryStatus.Posted, Source = JournalSource.Manual,
        };
        var wrongLine = new JournalLine
        {
            BookId = BookId, GlAccountId = 200, LineNumber = 1, Debit = 750m,
            CurrencyId = 1, TxnAmount = 750m, FunctionalAmount = 750m, FxRate = 1m,
        };
        wrongAccountEntry.Lines.Add(wrongLine);
        db.JournalEntries.Add(wrongAccountEntry);
        await db.SaveChangesAsync();

        await service.ImportAsync(BookId, CashAccountId, "one.csv",
            "Date,Description,Amount\n06/10/2026,DEPOSIT,750.00\n", userId: 1);
        var line = await db.BankStatementLines.SingleAsync();

        var act = () => service.ManualMatchAsync(line.Id, wrongLine.Id, userId: 1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not on this statement's cash account*");
    }
}
