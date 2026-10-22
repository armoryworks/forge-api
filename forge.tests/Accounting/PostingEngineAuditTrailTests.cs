using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// The posting engine writes the GL posting audit trail itself: one
/// <c>audit_log_entries</c> row per post, maker-checker submit and approval,
/// and reversal, anchored to the entry by a <c>journalEntryId</c> in the
/// details; idempotent replays write nothing, and a failed audit write never
/// fails the posting.
/// </summary>
public class PostingEngineAuditTrailTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int CashId = 100;
    private const int RevenueId = 101;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class ThrowingAuditWriter(AppDbContext db, Exception toThrow) : ISystemAuditWriter
    {
        public Task WriteAsync(
            string action, int userId, string? entityType = null, int? entityId = null,
            string? details = null, CancellationToken ct = default)
        {
            db.AuditLogEntries.Add(new AuditLogEntry { UserId = userId, Action = action, CreatedAt = DateTime.UnixEpoch });
            throw toThrow;
        }
    }

    private static ForgeGlPostingEngine CreateEngine(AppDbContext db, bool withWriter = true)
    {
        var clock = new SystemClock();
        return new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), clock,
            authorizer: null,
            auditWriter: withWriter ? new SystemAuditWriter(db, new HttpContextAccessor(), clock) : null);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
            Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount
            {
                Id = CashId, BookId = BookId, AccountNumber = "1000", Name = "Cash",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true,
            },
            new GlAccount
            {
                Id = RevenueId, BookId = BookId, AccountNumber = "4000", Name = "Revenue",
                AccountType = AccountType.Income, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true,
            });

        await db.SaveChangesAsync();
        return db;
    }

    private static PostingRequest Request(JournalSource source = JournalSource.Manual, string? key = null) => new()
    {
        BookId = BookId,
        EntryDate = new DateOnly(2026, 1, 15),
        Source = source,
        IdempotencyKey = key,
        CurrencyId = UsdId,
        Memo = "Audit trail JE",
        Lines =
        [
            new PostingLine { GlAccountId = CashId, Debit = 100m },
            new PostingLine { GlAccountId = RevenueId, Credit = 100m },
        ],
    };

    private static Task<List<AuditLogEntry>> AuditRowsFor(AppDbContext db, long entryId)
    {
        var withComma = $"\"journalEntryId\":{entryId},";
        var withBrace = $"\"journalEntryId\":{entryId}}}";
        return db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EntityType == nameof(JournalEntry)
                && a.Details != null
                && (a.Details.Contains(withComma) || a.Details.Contains(withBrace)))
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task PostAsync_WritesOnePostedRow_MatchingThePostingAuditPattern()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var entry = await engine.PostAsync(Request(), postedByUserId: 7);

        var rows = await AuditRowsFor(db, entry.Id);
        rows.Should().ContainSingle();
        rows[0].Action.Should().Be("GlJournalPosted");
        rows[0].UserId.Should().Be(7);
        rows[0].Details.Should().Contain("\"source\":\"Manual\"");
    }

    [Fact]
    public async Task PostAsync_DuplicateIdempotencyKey_AddsNoSecondRow()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var first = await engine.PostAsync(Request(JournalSource.Conversion, "conversion:opening:1:OPEN"), 7);
        var replay = await engine.PostAsync(Request(JournalSource.Conversion, "conversion:opening:1:OPEN"), 7);

        replay.Id.Should().Be(first.Id);
        (await AuditRowsFor(db, first.Id)).Should().ContainSingle();
        (await db.AuditLogEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PostPendingThenApprove_WritesSubmittedAndApprovedRows_WithTheirActors()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var pending = await engine.PostPendingAsync(Request(), submittedByUserId: 7);
        await engine.ApprovePendingAsync(pending.Id, approvedByUserId: 8);

        var rows = await AuditRowsFor(db, pending.Id);
        rows.Select(r => (r.Action, r.UserId)).Should().Equal(
            ("GlJournalSubmitted", 7),
            ("GlJournalApproved", 8));
        rows[1].Details.Should().Contain("\"status\":\"Posted\"");
    }

    [Fact]
    public async Task ResubmitDraftAsync_WritesASecondSubmittedRow_ForTheResubmitter()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var pending = await engine.PostPendingAsync(Request(), submittedByUserId: 7);
        pending.Status = JournalEntryStatus.Draft;
        await db.SaveChangesAsync();

        var resubmitted = await engine.ResubmitDraftAsync(pending.Id, Request(), submittedByUserId: 7);

        resubmitted.Status.Should().Be(JournalEntryStatus.PendingApproval);
        var rows = await AuditRowsFor(db, pending.Id);
        rows.Select(r => (r.Action, r.UserId)).Should().Equal(
            ("GlJournalSubmitted", 7),
            ("GlJournalSubmitted", 7));
        rows[1].Details.Should().Contain("\"status\":\"PendingApproval\"");
    }

    [Fact]
    public async Task ReverseAsync_WritesOneRowOnTheReversal_AndARepeatAddsNone()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(Request(), 7);
        var reversal = await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "Keyed in error", 9);

        var rows = await AuditRowsFor(db, reversal.Id);
        rows.Should().ContainSingle();
        rows[0].Action.Should().Be("GlJournalReversed");
        rows[0].UserId.Should().Be(9);
        rows[0].Details.Should().Contain($"\"reversalOfEntryId\":{original.Id}")
            .And.Contain("Keyed in error");

        var act = async () => await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "again", 9);
        await act.Should().ThrowAsync<PostingException>();
        (await AuditRowsFor(db, reversal.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task SubledgerPostAndReverseAsSystem_WriteEngineRows_WithTheActingUser()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var posted = await engine.PostAsSystemAsync(Request(JournalSource.AP, "ap:vendor_bill:5:approve"), actingUserId: 11);
        var reversal = await engine.ReverseAsSystemAsync(posted.Id, new DateOnly(2026, 1, 20), "Bill voided", 12);

        var postedRows = await AuditRowsFor(db, posted.Id);
        postedRows.Should().ContainSingle();
        postedRows[0].Action.Should().Be("GlJournalPosted");
        postedRows[0].UserId.Should().Be(11);
        postedRows[0].Details.Should().Contain("\"source\":\"AP\"");

        var reversalRows = await AuditRowsFor(db, reversal.Id);
        reversalRows.Should().ContainSingle();
        reversalRows[0].Action.Should().Be("GlJournalReversed");
        reversalRows[0].UserId.Should().Be(12);
    }

    [Fact]
    public async Task EngineWithoutWriter_WritesNoRows()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db, withWriter: false);

        var entry = await engine.PostAsync(Request(), 7);
        await engine.ReverseAsync(entry.Id, new DateOnly(2026, 1, 20), "undo", 7);

        (await db.AuditLogEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FailedAuditWrite_DoesNotFailThePosting_AndLeavesNoPendingAuditRow()
    {
        using var db = await SeedAsync();
        var clock = new SystemClock();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), clock,
            auditWriter: new ThrowingAuditWriter(db, new InvalidOperationException("audit store down")));

        var entry = await engine.PostAsync(Request(), 7);

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
        (await db.JournalEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CancelledAuditWrite_Propagates()
    {
        using var db = await SeedAsync();
        var clock = new SystemClock();
        var engine = new ForgeGlPostingEngine(
            db, new AccountDeterminationResolver(db), new FakeAllocator(), clock,
            auditWriter: new ThrowingAuditWriter(db, new OperationCanceledException()));

        var act = async () => await engine.PostAsync(Request(), 7);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
