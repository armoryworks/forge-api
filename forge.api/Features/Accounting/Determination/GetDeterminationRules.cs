using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting.Determination;

/// <summary>
/// The book-wide determination rules of <paramref name="BookId"/> (the active book when null), plus a row
/// for each configurable key the book has not mapped yet, so the Controller can see and fix a missing mapping.
/// </summary>
public record GetDeterminationRulesQuery(int? BookId = null) : IRequest<IReadOnlyList<DeterminationRuleModel>>;

public class GetDeterminationRulesHandler(AppDbContext db)
    : IRequestHandler<GetDeterminationRulesQuery, IReadOnlyList<DeterminationRuleModel>>
{
    public async Task<IReadOnlyList<DeterminationRuleModel>> Handle(GetDeterminationRulesQuery request, CancellationToken ct)
    {
        var bookId = request.BookId ?? await db.Books.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .Select(b => (int?)b.Id)
            .FirstOrDefaultAsync(ct);
        if (bookId is null)
            return [];

        var rules = await db.AccountDeterminationRules.AsNoTracking()
            .Where(r => r.BookId == bookId && r.ItemId == null && r.CategoryId == null && r.ValuationClassId == null)
            .Select(r => new { r.Key, r.GlAccountId, r.GlAccount.AccountNumber, r.GlAccount.Name })
            .ToListAsync(ct);

        var byKey = rules.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var keys = byKey.Keys.Union(ConfigurableDeterminationKeys.RequiredType.Keys).OrderBy(k => k, StringComparer.Ordinal);

        return keys
            .Select(k =>
            {
                var rule = byKey.GetValueOrDefault(k);
                var configurable = ConfigurableDeterminationKeys.RequiredType.TryGetValue(k, out var type);
                return new DeterminationRuleModel(
                    k, rule?.GlAccountId, rule?.AccountNumber, rule?.Name,
                    configurable, configurable ? type.ToString() : null);
            })
            .ToList();
    }
}
