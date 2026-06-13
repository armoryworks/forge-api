using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.Qbo;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// QB-001 Part B — the config-gated one-way QBO journal-summary push:
/// mapping-gap validation lists the unmapped account numbers; the push builds
/// ONE balanced JE from the mapped per-account nets (asserted through the
/// service seam); an overlapping re-push without force throws (→ 409); force
/// re-pushes; and the capability ships default-OFF outside the
/// BUILTIN⊥EXTERNAL mutex.
/// </summary>
public class QboExportTests
{
    private const int BookId = 1;
    private const int CashId = 100;
    private const int RevenueId = 101;

    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    /// <summary>Recording seam — captures what would be sent to QuickBooks.</summary>
    private sealed class RecordingPushService : IQboJournalPushService
    {
        public List<QboJournalEntryPush> Pushes { get; } = [];

        public Task<string> PushJournalEntryAsync(QboJournalEntryPush entry, CancellationToken ct)
        {
            Pushes.Add(entry);
            return Task.FromResult($"QBO-{Pushes.Count}");
        }
    }

    private static AppDbContext Seed(bool mapCash = true, bool mapRevenue = true)
    {
        var db = TestDbContextFactory.Create();

        db.GlAccounts.AddRange(
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash" },
            new GlAccount { Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue" });

        db.JournalEntries.Add(new JournalEntry
        {
            Id = 1, BookId = BookId, EntryNumber = 1, EntryDate = new DateOnly(2026, 6, 5),
            FiscalPeriodId = 1, FiscalYearId = 1, CurrencyId = 1,
            Source = JournalSource.Manual, Status = JournalEntryStatus.Posted,
        });
        db.JournalLines.AddRange(
            new JournalLine
            {
                Id = 11, JournalEntryId = 1, BookId = BookId, LineNumber = 1, GlAccountId = CashId,
                Debit = 250m, Credit = 0m, CurrencyId = 1, TxnAmount = 250m, FunctionalAmount = 250m, FxRate = 1m,
            },
            new JournalLine
            {
                Id = 12, JournalEntryId = 1, BookId = BookId, LineNumber = 2, GlAccountId = RevenueId,
                Debit = 0m, Credit = 250m, CurrencyId = 1, TxnAmount = 250m, FunctionalAmount = 250m, FxRate = 1m,
            });

        if (mapCash)
            db.QboAccountMaps.Add(new QboAccountMap { GlAccountId = CashId, QboAccountId = "79", QboAccountName = "QB Checking" });
        if (mapRevenue)
            db.QboAccountMaps.Add(new QboAccountMap { GlAccountId = RevenueId, QboAccountId = "80", QboAccountName = "QB Sales" });

        db.SaveChanges();
        return db;
    }

    private static PushQboJournalSummaryHandler Handler(AppDbContext db, RecordingPushService push)
        => new(db, new TrialBalanceService(db), push, new SystemClock());

    [Fact]
    public async Task Push_WithUnmappedNonzeroAccount_ThrowsListingTheGaps()
    {
        await using var db = Seed(mapCash: true, mapRevenue: false);
        var push = new RecordingPushService();

        var act = () => Handler(db, push).Handle(
            new PushQboJournalSummaryCommand(BookId, From, To), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("4000").And.NotContain("1000,");
        push.Pushes.Should().BeEmpty();
        db.QboExportLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Push_BuildsOneBalancedJournalEntry_FromTheMappedSummary_AndLogsIt()
    {
        await using var db = Seed();
        db.CurrentUserId = 42;
        var push = new RecordingPushService();

        var result = await Handler(db, push).Handle(
            new PushQboJournalSummaryCommand(BookId, From, To), CancellationToken.None);

        var sent = push.Pushes.Should().ContainSingle().Subject;
        sent.Memo.Should().Be("Forge GL summary 2026-06-01..2026-06-30");
        sent.TxnDate.Should().Be(To);
        sent.Lines.Should().HaveCount(2);
        sent.Lines.Where(l => l.IsDebit).Sum(l => l.Amount)
            .Should().Be(sent.Lines.Where(l => !l.IsDebit).Sum(l => l.Amount), "the pushed JE must balance");
        sent.Lines.Single(l => l.IsDebit).QboAccountId.Should().Be("79");
        sent.Lines.Single(l => !l.IsDebit).QboAccountId.Should().Be("80");

        result.QboDocId.Should().Be("QBO-1");
        result.TotalDebit.Should().Be(250m);
        result.LineCount.Should().Be(2);

        var log = db.QboExportLogs.Should().ContainSingle().Subject;
        log.BookId.Should().Be(BookId);
        log.FromDate.Should().Be(From);
        log.ToDate.Should().Be(To);
        log.QboDocId.Should().Be("QBO-1");
        log.TotalDebit.Should().Be(250m);
        log.PushedBy.Should().Be(42);
    }

    [Fact]
    public async Task Push_OverlappingRange_WithoutForce_Throws()
    {
        await using var db = Seed();
        var push = new RecordingPushService();
        var handler = Handler(db, push);

        await handler.Handle(new PushQboJournalSummaryCommand(BookId, From, To), CancellationToken.None);

        // A second, partially-overlapping range without force must refuse.
        var act = () => handler.Handle(
            new PushQboJournalSummaryCommand(BookId, new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 15)),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("force=true").And.Contain("QBO-1");
        push.Pushes.Should().HaveCount(1, "the overlapping push must be blocked before reaching QuickBooks");
    }

    [Fact]
    public async Task Push_OverlappingRange_WithForce_RePushes()
    {
        await using var db = Seed();
        var push = new RecordingPushService();
        var handler = Handler(db, push);

        await handler.Handle(new PushQboJournalSummaryCommand(BookId, From, To), CancellationToken.None);
        var second = await handler.Handle(
            new PushQboJournalSummaryCommand(BookId, From, To, Force: true), CancellationToken.None);

        second.QboDocId.Should().Be("QBO-2");
        push.Pushes.Should().HaveCount(2);
        db.QboExportLogs.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mappings_UpsertIsUniquePerAccount_AndLeftJoinShowsGaps()
    {
        await using var db = Seed(mapCash: false, mapRevenue: false);

        var upsert = new UpsertQboAccountMapHandler(db);
        await upsert.Handle(new UpsertQboAccountMapCommand(CashId, "79", "QB Checking"), CancellationToken.None);
        await upsert.Handle(new UpsertQboAccountMapCommand(CashId, "81", "QB Savings"), CancellationToken.None);

        db.QboAccountMaps.Should().ContainSingle().Which.QboAccountId.Should().Be("81");

        var rows = await new GetQboAccountMappingsHandler(db)
            .Handle(new GetQboAccountMappingsQuery(BookId), CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Single(r => r.GlAccountId == CashId).QboAccountId.Should().Be("81");
        rows.Single(r => r.GlAccountId == RevenueId).QboAccountId.Should().BeNull("unmapped accounts must still appear");
    }

    [Fact]
    public void Capability_IsRegisteredDefaultOff_OutsideTheAccountingModeMutex()
    {
        var cap = CapabilityCatalog.All.Should()
            .ContainSingle(c => c.Code == "CAP-ACCT-QBO-EXPORT").Subject;

        cap.IsDefaultOn.Should().BeFalse("the QBO push is config-gated per the §10 ratification");
        cap.Area.Should().Be("ACCT");

        // Downstream export FROM the built-in GL — NOT a party to BUILTIN ⊥ EXTERNAL.
        CapabilityCatalogRelations.Mutexes.Should().NotContain(m =>
            m.From == "CAP-ACCT-QBO-EXPORT" || m.To == "CAP-ACCT-QBO-EXPORT");

        CapabilityCatalogRelations.Dependencies.Should().Contain(d =>
            d.From == "CAP-ACCT-QBO-EXPORT" && d.To == "CAP-ACCT-FULLGL");
    }
}
