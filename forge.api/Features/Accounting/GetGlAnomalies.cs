using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Read-only anomaly scan (§5A Accounting-AI advisory) — a reviewer's "look at these" list over the
/// posted manual journal entries of a book. Deterministic, explainable rules (never AI-authored, never
/// posts); a flagged entry can then be narrated via <c>GET journal-entries/{id}/explain</c>. DARK
/// behind <c>CAP-ACCT-FULLGL</c>.
/// <para>
/// Rules (extensible; the threshold is caller-supplied, not a hidden constant):
/// (1) a manual line posted directly to a <b>control account</b> — control accounts should move only
/// via their sub-ledgers, so a hand-posting there is a classic reconciliation red flag; and
/// (2) a <b>large manual entry</b> at/above <c>LargeManualThreshold</c>.
/// </para>
/// </summary>
[RequiresCapability("CAP-ACCT-FULLGL")]
public record GetGlAnomaliesQuery(
    int BookId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    decimal LargeManualThreshold = 100_000m)
    : IRequest<IReadOnlyList<GlAnomaly>>;

public class GetGlAnomaliesHandler(AppDbContext db)
    : IRequestHandler<GetGlAnomaliesQuery, IReadOnlyList<GlAnomaly>>
{
    public async Task<IReadOnlyList<GlAnomaly>> Handle(GetGlAnomaliesQuery request, CancellationToken ct)
    {
        var q = db.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.BookId == request.BookId
                && e.Source == JournalSource.Manual
                && e.Status == JournalEntryStatus.Posted);

        if (request.FromDate is { } from) q = q.Where(e => e.EntryDate >= from);
        if (request.ToDate is { } to) q = q.Where(e => e.EntryDate <= to);

        var entries = await q
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.EntryNumber)
            .ToListAsync(ct);

        var accountIds = entries.SelectMany(e => e.Lines).Select(l => l.GlAccountId).Distinct().ToList();
        var controlAccountNumberById = (await db.GlAccounts
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(a => accountIds.Contains(a.Id) && a.IsControlAccount)
                .Select(a => new { a.Id, a.AccountNumber })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.AccountNumber);

        var results = new List<GlAnomaly>();
        foreach (var entry in entries)
        {
            var flags = new List<string>();
            var totalDebit = entry.Lines.Sum(l => l.Debit);

            var controlHits = entry.Lines
                .Where(l => controlAccountNumberById.ContainsKey(l.GlAccountId))
                .Select(l => controlAccountNumberById[l.GlAccountId])
                .Distinct()
                .ToList();
            if (controlHits.Count > 0)
                flags.Add($"Manual posting to control account(s) {string.Join(", ", controlHits)} — control accounts should move via their sub-ledgers.");

            if (totalDebit >= request.LargeManualThreshold)
                flags.Add($"Large manual entry ({totalDebit:0.##}) at/above the {request.LargeManualThreshold:0.##} review threshold.");

            if (flags.Count > 0)
                results.Add(new GlAnomaly(entry.Id, entry.EntryNumber, entry.EntryDate, entry.Source.ToString(), totalDebit, flags));
        }

        return results;
    }
}

/// <summary>One flagged entry — the reviewer decides; a plain-language read is available via the explain endpoint.</summary>
public record GlAnomaly(
    long EntryId,
    long EntryNumber,
    DateOnly EntryDate,
    string Source,
    decimal TotalDebit,
    IReadOnlyList<string> Flags);
