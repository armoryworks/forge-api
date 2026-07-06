using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Read-only <b>Accounting AI advisory</b>: a plain-language explanation of a journal entry for a
/// reviewer (ai-fleet-orchestration D / ACCOUNTING_SUITE_PLAN §5A). This is the advisory seam and it
/// enforces the standing guardrail architecturally — it ONLY reads the ledger and asks the LLM for a
/// narrative; it has no dependency on <c>IPostingEngine</c> and never writes. When the model is
/// offline it degrades to a deterministic, non-AI summary (<c>AiAvailable=false</c>). DARK behind
/// <c>CAP-ACCT-FULLGL</c>; the AI itself degrades gracefully when the assistant is unavailable.
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record ExplainJournalEntryQuery(int BookId, long EntryId) : IRequest<JournalEntryExplanation>;

public class ExplainJournalEntryHandler(AppDbContext db, IAiService ai)
    : IRequestHandler<ExplainJournalEntryQuery, JournalEntryExplanation>
{
    public async Task<JournalEntryExplanation> Handle(ExplainJournalEntryQuery request, CancellationToken ct)
    {
        var entry = await db.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == request.EntryId && e.BookId == request.BookId, ct);

        if (entry is null)
            throw new KeyNotFoundException($"Journal entry {request.EntryId} not found in book {request.BookId}.");

        var accountIds = entry.Lines.Select(l => l.GlAccountId).Distinct().ToList();
        var accountLabelById = (await db.GlAccounts
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(a => accountIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AccountNumber, a.Name })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => $"{a.AccountNumber} {a.Name}");

        var deterministic = BuildDeterministicSummary(entry, accountLabelById);

        // Advisory only, and only when the assistant is reachable — otherwise return the deterministic
        // read so the reviewer still gets something factual.
        if (!await ai.IsAvailableAsync(ct))
            return new JournalEntryExplanation(entry.Id, deterministic, false, deterministic);

        const string system =
            "You are a concise accounting-controls assistant. Explain the given double-entry journal " +
            "entry in 2-4 plain-language sentences for a reviewer: what it appears to record, and note " +
            "anything worth a second look (unusual account pairing, large amount, manual source). Use " +
            "only the data provided — do not invent figures or accounts. You are advisory only: you " +
            "never post, approve, or modify anything.";
        var explanation = (await ai.GenerateTextAsync(BuildPrompt(entry, deterministic), system, 0.2, ct)).Trim();

        return new JournalEntryExplanation(entry.Id, explanation, true, deterministic);
    }

    private static string BuildDeterministicSummary(JournalEntry entry, IReadOnlyDictionary<int, string> accountLabelById)
    {
        var lines = entry.Lines.OrderBy(l => l.LineNumber).Select(l =>
        {
            var account = accountLabelById.TryGetValue(l.GlAccountId, out var label) ? label : $"account {l.GlAccountId}";
            return l.Debit > 0 ? $"Dr {account} {l.Debit:0.##}" : $"Cr {account} {l.Credit:0.##}";
        });

        var memo = string.IsNullOrWhiteSpace(entry.Memo) ? "" : $" — {entry.Memo}";
        return $"Entry #{entry.EntryNumber} ({entry.EntryDate:yyyy-MM-dd}, {entry.Source}, {entry.Status}): "
             + string.Join("; ", lines) + memo + ".";
    }

    private static string BuildPrompt(JournalEntry entry, string deterministic)
    {
        var totalDebits = entry.Lines.Sum(l => l.Debit);
        var source = entry.SourceType is null ? entry.Source.ToString() : $"{entry.Source} ({entry.SourceType} #{entry.SourceId})";
        return $"""
            Journal entry to explain (double-entry; total debits equal total credits):
            {deterministic}
            Source: {source}
            Total debits = total credits = {totalDebits:0.##}.

            Explain this entry for a reviewer in 2-4 sentences and flag anything worth a second look.
            """;
    }
}

/// <summary>
/// Advisory result. <c>Explanation</c> is the AI narrative when available, else the deterministic read.
/// <c>DeterministicSummary</c> is always the non-AI read so the UI can show provenance / a fallback.
/// </summary>
public record JournalEntryExplanation(long EntryId, string Explanation, bool AiAvailable, string DeterministicSummary);
