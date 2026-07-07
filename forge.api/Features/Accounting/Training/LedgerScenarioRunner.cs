using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Training;

/// <summary>Per-validator outcome, so the UI can show exactly which condition is still unmet.</summary>
public record ValidatorResult(string Type, bool Passed);

/// <summary>Scenario check outcome: pass = every validator passed against the sandbox ledger.</summary>
public record ScenarioCheckResult(string ScenarioId, bool Passed, IReadOnlyList<ValidatorResult> Validators);

/// <summary>
/// §5A.4: validates a scenario's LEDGER END-STATE (never the click path) against the TRAINING book.
/// Six validator types (design doc §2): entryPosted, entryReversed, entryLinked, memoRequired,
/// accountBalance, trialBalanced.
/// </summary>
public interface ILedgerScenarioRunner
{
    Task<ScenarioCheckResult> CheckAsync(TrainingScenario scenario, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class LedgerScenarioRunner(AppDbContext db) : ILedgerScenarioRunner
{
    public async Task<ScenarioCheckResult> CheckAsync(TrainingScenario scenario, CancellationToken ct = default)
    {
        var bookId = await db.Books.AsNoTracking()
            .Where(b => b.Code == TrainingSandboxService.BookCode)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (bookId is null)
            return new ScenarioCheckResult(scenario.Id, false, [.. scenario.Validators.Select(v => new ValidatorResult(v.Type, false))]);

        // One filter-immune load; every validator evaluates in memory against it.
        var entries = await db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.BookId == bookId)
            .ToListAsync(ct);
        var accountNumberById = (await db.GlAccounts.AsNoTracking().IgnoreQueryFilters()
                .Where(a => a.BookId == bookId)
                .Select(a => new { a.Id, a.AccountNumber })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.AccountNumber);

        var results = scenario.Validators
            .Select(v => new ValidatorResult(v.Type, Evaluate(v, entries, accountNumberById)))
            .ToList();
        return new ScenarioCheckResult(scenario.Id, results.All(r => r.Passed), results);
    }

    private static bool Evaluate(ScenarioValidator v, List<JournalEntry> entries, Dictionary<int, string> acct)
    {
        bool MemoMatch(JournalEntry e) =>
            v.MemoContains is null || (e.Memo ?? "").Contains(v.MemoContains, StringComparison.OrdinalIgnoreCase);
        bool TouchesAccount(JournalEntry e, string number) => e.Lines.Any(l => acct.GetValueOrDefault(l.GlAccountId) == number);

        switch (v.Type)
        {
            case "entryPosted":
                return entries.Any(e =>
                    e.Status == JournalEntryStatus.Posted
                    && MemoMatch(e)
                    && (v.AccountNumber is null || TouchesAccount(e, v.AccountNumber))
                    && (v.DrAccountNumber is null || e.Lines.Any(l => l.Debit > 0 && acct.GetValueOrDefault(l.GlAccountId) == v.DrAccountNumber && (v.Amount is null || l.Debit == v.Amount)))
                    && (v.CrAccountNumber is null || e.Lines.Any(l => l.Credit > 0 && acct.GetValueOrDefault(l.GlAccountId) == v.CrAccountNumber && (v.Amount is null || l.Credit == v.Amount))));

            case "entryReversed":
                return entries.Any(e =>
                    e.Status == JournalEntryStatus.Reversed
                    && MemoMatch(e)
                    && (v.AccountNumber is null || TouchesAccount(e, v.AccountNumber)));

            case "entryLinked":
                // The reversal pair is intact: a Reversed entry pointing at its reversal, whose
                // reversal points back — the drill-back the ledger UI renders as chips.
                return entries.Any(e =>
                    e.Status == JournalEntryStatus.Reversed
                    && MemoMatch(e)
                    && e.ReversedByEntryId is long rid
                    && entries.Any(r => r.Id == rid && r.ReversalOfEntryId == e.Id));

            case "memoRequired":
                var newestManual = entries
                    .Where(e => e.Source == JournalSource.Manual && e.Status == JournalEntryStatus.Posted)
                    .OrderByDescending(e => e.Id)
                    .FirstOrDefault();
                return newestManual is not null && !string.IsNullOrWhiteSpace(newestManual.Memo);

            case "accountBalance":
                if (v.AccountNumber is null || v.Expected is null) return false;
                var net = entries
                    .Where(e => e.Status is JournalEntryStatus.Posted or JournalEntryStatus.Reversed)
                    .SelectMany(e => e.Lines)
                    .Where(l => acct.GetValueOrDefault(l.GlAccountId) == v.AccountNumber)
                    .Sum(l => l.Debit - l.Credit);
                return net == v.Expected;

            case "trialBalanced":
                var posted = entries.Where(e => e.Status is JournalEntryStatus.Posted or JournalEntryStatus.Reversed).SelectMany(e => e.Lines).ToList();
                return posted.Sum(l => l.Debit) == posted.Sum(l => l.Credit);

            default:
                return false; // unknown validator type never passes — fails loudly in the scenario result
        }
    }
}
