using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.Training;
using Forge.Core.Entities;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// §5A.4 training sandbox: the seeder builds the TRAINING book through the REAL posting engine
/// (opening + one quarter + plants P1–P5, always balanced), and the scenario runner validates
/// ledger END-STATE — failing before the learner's correction and passing after a real
/// reverse-and-repost through the engine. (Reset's DELETE path needs the Postgres trigger
/// carve-out and ExecuteDelete, so it is verified live, not on InMemory.)
/// </summary>
public class TrainingSandboxTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default) => Task.FromResult(_next++);
    }

    private static (AppDbContext Db, IPostingEngine Engine, TrainingSandboxService Sandbox) Create()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar" });
        db.SaveChanges();
        var engine = new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new FixedClock());
        return (db, engine, new TrainingSandboxService(db, engine));
    }

    [Fact]
    public async Task Seeder_builds_a_balanced_quarter_with_the_planted_errors()
    {
        var (db, _, sandbox) = Create();
        var state = await sandbox.EnsureSeededAsync(actorUserId: 7);

        state.Seeded.Should().BeTrue();
        state.EntryCount.Should().BeGreaterThan(25);

        var bookId = state.BookId!.Value;
        (await db.GlAccounts.CountAsync(a => a.BookId == bookId)).Should().Be(13);

        var lines = await db.JournalLines.IgnoreQueryFilters().Where(l => l.BookId == bookId).ToListAsync();
        lines.Sum(l => l.Debit).Should().Be(lines.Sum(l => l.Credit), "the sandbox always balances");

        var memos = await db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.BookId == bookId).Select(e => e.Memo!).ToListAsync();
        memos.Should().Contain(m => m.Contains("April power bill"));      // P1 miscode
        memos.Count(m => m.Contains("City Power (Jun)")).Should().Be(2);  // P2 duplicate
        memos.Should().Contain(m => m.Contains("check deposit"));         // P3 NSF
        memos.Should().Contain(m => m.Contains("Adjust AR"));             // P4 control hand-post
        memos.Should().Contain(m => m == "adjust to match bank");         // P5 no narration

        // Idempotent: a second EnsureSeeded never double-posts (per-line idempotency keys).
        var again = await sandbox.EnsureSeededAsync(7);
        again.EntryCount.Should().Be(state.EntryCount);
    }

    [Fact]
    public async Task Scenario_fails_before_the_fix_and_passes_after_a_real_reverse_and_repost()
    {
        var (db, engine, sandbox) = Create();
        var state = await sandbox.EnsureSeededAsync(7);
        var bookId = state.BookId!.Value;
        var runner = new LedgerScenarioRunner(db);

        var scenario = new TrainingScenario(
            "fix-miscoded-utilities", "both", 20, "t", "b", null, [],
            [
                new ScenarioValidator("entryReversed", MemoContains: "power bill", AccountNumber: "60100"),
                new ScenarioValidator("entryLinked", MemoContains: "power bill"),
                new ScenarioValidator("entryPosted", DrAccountNumber: "60300", CrAccountNumber: "10100", Amount: 842.17m),
                new ScenarioValidator("trialBalanced"),
            ],
            "s");

        (await runner.CheckAsync(scenario)).Passed.Should().BeFalse("the plant is uncorrected");

        // The learner's fix, exactly as the UI does it: reverse the miscoded entry…
        var plant = await db.JournalEntries.IgnoreQueryFilters()
            .FirstAsync(e => e.BookId == bookId && e.Memo!.Contains("April power bill"));
        await engine.ReverseAsync(plant.Id, new DateOnly(2026, 7, 7), "Coded to Rent in error", reversedByUserId: 7);

        // …then post the correction.
        var accounts = await db.GlAccounts.Where(a => a.BookId == bookId).ToDictionaryAsync(a => a.AccountNumber, a => a.Id);
        await engine.PostAsync(new Forge.Core.Models.Accounting.PostingRequest
        {
            BookId = bookId,
            EntryDate = new DateOnly(2026, 7, 7),
            Source = JournalSource.Manual,
            CurrencyId = 1,
            Memo = "Correction of April power bill — Utilities, not Rent",
            Lines =
            [
                new Forge.Core.Models.Accounting.PostingLine { GlAccountId = accounts["60300"], Debit = 842.17m },
                new Forge.Core.Models.Accounting.PostingLine { GlAccountId = accounts["10100"], Credit = 842.17m },
            ],
        }, 7);

        var result = await runner.CheckAsync(scenario);
        result.Validators.Where(v => !v.Passed).Should().BeEmpty();
        result.Passed.Should().BeTrue();
    }

    [Fact]
    public void Shipped_scenario_catalog_loads_and_references_known_validator_types()
    {
        var provider = new TrainingScenarioProvider();
        provider.All.Should().HaveCount(6);
        provider.All.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        string[] known = ["entryPosted", "entryReversed", "entryLinked", "memoRequired", "accountBalance", "trialBalanced"];
        provider.All.SelectMany(s => s.Validators).Should().OnlyContain(v => known.Contains(v.Type));
    }
}
