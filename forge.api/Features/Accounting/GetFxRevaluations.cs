using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <summary>Past unrealized FX revaluation runs of a book (journal entries with SourceType FxRevaluation), newest first.</summary>
public record GetFxRevaluationsQuery(int BookId) : IRequest<IReadOnlyList<FxRevaluationRunModel>>;

public class GetFxRevaluationsHandler(AppDbContext db)
    : IRequestHandler<GetFxRevaluationsQuery, IReadOnlyList<FxRevaluationRunModel>>
{
    public async Task<IReadOnlyList<FxRevaluationRunModel>> Handle(GetFxRevaluationsQuery request, CancellationToken ct)
    {
        var revalAccountId = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == request.BookId && r.Key == "FX_REVALUATION")
            .Select(r => (int?)r.GlAccountId)
            .FirstOrDefaultAsync(ct);

        var entries = await db.JournalEntries.AsNoTracking()
            .Where(e => e.BookId == request.BookId
                && e.SourceType == "FxRevaluation"
                && e.ReversalOfEntryId == null
                && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed))
            .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.EntryNumber,
                e.EntryDate,
                e.SourceId,
                e.Memo,
                e.Status,
                e.PostedAt,
                Adjustment = e.Lines.Where(l => l.GlAccountId == revalAccountId).Sum(l => l.Debit - l.Credit),
            })
            .ToListAsync(ct);

        var currencyIds = entries.Where(e => e.SourceId != null).Select(e => (int)e.SourceId!.Value).Distinct().ToList();
        var codes = await db.Currencies.AsNoTracking()
            .Where(c => currencyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);

        return entries
            .Select(e => new FxRevaluationRunModel(
                e.Id, e.EntryNumber, e.EntryDate, (int)(e.SourceId ?? 0),
                codes.GetValueOrDefault((int)(e.SourceId ?? 0)),
                e.Adjustment, e.Memo, e.Status == JournalEntryStatus.Reversed, e.PostedAt))
            .ToList();
    }
}
