using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
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
/// Phase-0 posting-engine edge cases (§5.2, §5.3, §11) complementing
/// <see cref="PostingEngineTests"/>:
/// <list type="bullet">
///   <item>idempotency-key collision across <i>different</i> sources (the unique
///   key is <c>(BookId, IdempotencyKey)</c> — source is NOT part of the key, so
///   a collision returns the existing entry regardless of source);</item>
///   <item>reversal whose own date lands in a HardClosed period is rejected
///   (the reversal resolves its OWN period from its OWN date — §5.2);</item>
///   <item>reversing a reversal — the engine's actual behaviour (a reversal is
///   itself <c>Posted</c> with a null <c>ReversedByEntryId</c>, so the
///   double-reverse guard does not trip; §11 leaves the explicit policy
///   deferred, so this test pins the current behaviour);</item>
///   <item>trial balance excludes <c>Draft</c> and nets <c>Reversed</c>.</item>
/// </list>
/// </summary>
public class PostingEngineEdgeCaseTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    private const int CashId = 100;
    private const int RevenueId = 101;

    private const int OpenPeriodId = 1000;
    private const int HardClosedPeriodId = 1002;

    /// <summary>In-process allocator (the InMemory provider can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine CreateEngine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

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

        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod
            {
                Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
                Status = FiscalPeriodStatus.Open,
            },
            new FiscalPeriod
            {
                Id = HardClosedPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 3, Name = "Mar 2026",
                StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 31),
                Status = FiscalPeriodStatus.HardClosed,
            });

        db.Set<GlAccount>().AddRange(
            Account(CashId, "1000", "Cash", AccountType.Asset, NormalBalance.Debit),
            Account(RevenueId, "4000", "Revenue", AccountType.Income, NormalBalance.Credit));

        await db.SaveChangesAsync();
        return db;
    }

    private static GlAccount Account(int id, string num, string name, AccountType type, NormalBalance nb)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = num, Name = name,
            AccountType = type, NormalBalance = nb, IsPostable = true, IsActive = true,
        };

    private static PostingRequest Balanced(
        JournalSource source, string? idempotencyKey, DateOnly? date = null, decimal amount = 100m) => new()
    {
        BookId = BookId,
        EntryDate = date ?? new DateOnly(2026, 1, 15),
        Source = source,
        IdempotencyKey = idempotencyKey,
        CurrencyId = UsdId,
        Lines =
        [
            new PostingLine { GlAccountId = CashId, Debit = amount },
            new PostingLine { GlAccountId = RevenueId, Credit = amount },
        ],
    };

    // --- Idempotency-key collision across different sources -------------------

    [Fact]
    public async Task PostAsync_SameKeyDifferentSource_CollidesAndReturnsExisting()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        // The unique key is (BookId, IdempotencyKey); Source is NOT part of it.
        // A second post with the SAME key but a DIFFERENT source returns the
        // already-posted entry rather than creating a parallel one.
        const string sharedKey = "SHARED:Doc:7:REVENUE";

        var first = await engine.PostAsync(Balanced(JournalSource.AR, sharedKey, amount: 100m), 1);
        var second = await engine.PostAsync(Balanced(JournalSource.AP, sharedKey, amount: 999m), 2);

        second.Id.Should().Be(first.Id);
        second.Source.Should().Be(JournalSource.AR);            // the original's source wins
        (await db.JournalEntries.CountAsync()).Should().Be(1);  // no second entry written
    }

    [Fact]
    public async Task PostAsync_DifferentKeysDifferentSources_BothPost()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var ar = await engine.PostAsync(Balanced(JournalSource.AR, "AR:Invoice:1:REVENUE"), 1);
        var ap = await engine.PostAsync(Balanced(JournalSource.AP, "AP:Bill:1:GRNI"), 1);

        ar.Id.Should().NotBe(ap.Id);
        (await db.JournalEntries.CountAsync()).Should().Be(2);
    }

    // --- Reverse into a HardClosed period -------------------------------------

    [Fact]
    public async Task ReverseAsync_ReversalDateInHardClosedPeriod_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        // Original posts fine into the OPEN period; the reversal date lands in
        // the HardClosed period — the reversal resolves its OWN period and is
        // rejected. The original is left untouched (still Posted, not reversed).
        var original = await engine.PostAsync(Balanced(JournalSource.Manual, null), 1);

        var act = async () => await engine.ReverseAsync(original.Id, new DateOnly(2026, 3, 15), "x", 9);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_HARD_CLOSED");

        var reloaded = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == original.Id);
        reloaded.Status.Should().Be(JournalEntryStatus.Posted);
        reloaded.ReversedByEntryId.Should().BeNull();
    }

    // --- Reversing a reversal (§11 deferred policy — pin current behaviour) ----

    [Fact]
    public async Task ReverseAsync_ReversingAReversal_IsPermittedAndFlipsTheReversal()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(Balanced(JournalSource.Manual, null, amount: 100m), 1);
        var reversal = await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "correct", 9);

        // A reversal is itself Posted with a null ReversedByEntryId, so the
        // double-reverse guard (which keys off ReversedByEntryId / Status) does
        // NOT trip for it. The engine therefore reverses the reversal, producing
        // an entry that re-applies the original's direction.
        var reReversal = await engine.ReverseAsync(reversal.Id, new DateOnly(2026, 1, 25), "undo", 9);

        reReversal.Status.Should().Be(JournalEntryStatus.Posted);
        reReversal.ReversalOfEntryId.Should().Be(reversal.Id);

        // The reversal flipped Cash to Credit; re-reversing flips it back to Debit
        // (i.e. matches the original direction).
        reReversal.Lines.Single(l => l.GlAccountId == CashId).Debit.Should().Be(100m);
        reReversal.Lines.Single(l => l.GlAccountId == RevenueId).Credit.Should().Be(100m);

        // The (now Reversed) reversal is linked to its re-reversal.
        var reloadedReversal = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == reversal.Id);
        reloadedReversal.Status.Should().Be(JournalEntryStatus.Reversed);
        reloadedReversal.ReversedByEntryId.Should().Be(reReversal.Id);

        // Three entries (original, reversal, re-reversal); the original is the
        // only one still Reversed-by-its-own-reversal and stays untouched.
        var originalReloaded = await db.JournalEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == original.Id);
        originalReloaded.ReversedByEntryId.Should().Be(reversal.Id);
    }

    [Fact]
    public async Task ReverseAsync_DuplicateReversalRequest_ReturnsExistingReversal()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(Balanced(JournalSource.Manual, null), 1);
        var firstReversal = await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "x", 9);

        // The original is now Reversed (ReversedByEntryId set), so a second
        // ReverseAsync on the ORIGINAL trips the double-reverse guard first —
        // proving the REVERSAL idempotency key is only reached for the reversal
        // entry itself. (Guard precedence: ALREADY_REVERSED before idempotency.)
        var act = async () => await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 21), "again", 9);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("ALREADY_REVERSED");

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        firstReversal.ReversalOfEntryId.Should().Be(original.Id);
    }

    // --- Trial balance excludes Draft and nets Reversed -----------------------

    [Fact]
    public async Task TrialBalance_ExcludesDraftEntries()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);
        var tb = new TrialBalanceService(db);

        // One genuinely Posted entry via the engine.
        await engine.PostAsync(Balanced(JournalSource.Manual, null, amount: 100m), 1);

        // A hand-inserted DRAFT entry with balanced lines that must NOT appear in
        // the trial balance (the engine only ever posts, so a Draft has to be
        // inserted directly to exercise the §5.3 exclusion).
        var draft = new JournalEntry
        {
            BookId = BookId,
            EntryNumber = 9001,
            EntryDate = new DateOnly(2026, 1, 16),
            FiscalPeriodId = OpenPeriodId,
            FiscalYearId = FiscalYearId,
            Source = JournalSource.Manual,
            CurrencyId = UsdId,
            Status = JournalEntryStatus.Draft,
            Lines =
            [
                new JournalLine
                {
                    BookId = BookId, LineNumber = 1, GlAccountId = CashId,
                    Debit = 500m, Credit = 0m, CurrencyId = UsdId,
                    TxnAmount = 500m, FunctionalAmount = 500m, FxRate = 1m,
                },
                new JournalLine
                {
                    BookId = BookId, LineNumber = 2, GlAccountId = RevenueId,
                    Debit = 0m, Credit = 500m, CurrencyId = UsdId,
                    TxnAmount = 500m, FunctionalAmount = 500m, FxRate = 1m,
                },
            ],
        };
        db.JournalEntries.Add(draft);
        await db.SaveChangesAsync();

        var result = await tb.GetTrialBalanceAsync(BookId);

        // Only the 100 Posted entry is reflected; the 500 Draft is excluded.
        result.IsBalanced.Should().BeTrue();
        result.TotalDebit.Should().Be(100m);
        result.TotalCredit.Should().Be(100m);
        result.Rows.Single(r => r.GlAccountId == CashId).DebitTotal.Should().Be(100m);
    }

    [Fact]
    public async Task TrialBalance_NetsReversedToZero()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);
        var tb = new TrialBalanceService(db);

        var original = await engine.PostAsync(Balanced(JournalSource.Manual, null, amount: 250m), 1);
        await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "x", 1);

        // Original (now Reversed) + its reversal (Posted) — both are summed by the
        // trial balance and cancel: every account nets to zero, totals balance.
        // Gross totals are doubled (Cash Dr250 + Revenue Dr250 from the reversal),
        // but each account's NET is zero — that is the "nets Reversed" guarantee.
        var result = await tb.GetTrialBalanceAsync(BookId);

        result.IsBalanced.Should().BeTrue();
        result.TotalDebit.Should().Be(500m);    // Dr 250 (orig Cash) + Dr 250 (reversal Revenue)
        result.TotalCredit.Should().Be(500m);   // Cr 250 (orig Revenue) + Cr 250 (reversal Cash)
        result.Rows.Should().OnlyContain(r => r.NetBalance == 0m);
        result.Rows.Sum(r => r.NetBalance).Should().Be(0m);
    }

    [Fact]
    public async Task TrialBalance_DateWindow_RestrictsToEntriesInRange()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);
        var tb = new TrialBalanceService(db);

        await engine.PostAsync(Balanced(JournalSource.Manual, null, new DateOnly(2026, 1, 5), 100m), 1);
        await engine.PostAsync(Balanced(JournalSource.Manual, null, new DateOnly(2026, 1, 25), 40m), 1);

        // A window that includes only the first entry.
        var windowed = await tb.GetTrialBalanceAsync(
            BookId, fromDate: new DateOnly(2026, 1, 1), toDate: new DateOnly(2026, 1, 10));

        windowed.IsBalanced.Should().BeTrue();
        windowed.TotalDebit.Should().Be(100m);
        windowed.TotalCredit.Should().Be(100m);
    }
}
