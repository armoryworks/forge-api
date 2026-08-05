using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.I18nLabelOverrides;

/// <summary>
/// Returns the merge map the UI layers over the shipped i18n JSON catalogs:
/// language code → (key → override value). Pending machine translations are
/// excluded — their placeholder text is source-language and must not shadow
/// the shipped translation.
/// </summary>
public record GetActiveI18nLabelOverridesQuery : IRequest<Dictionary<string, Dictionary<string, string>>>;

public class GetActiveI18nLabelOverridesHandler(AppDbContext db)
    : IRequestHandler<GetActiveI18nLabelOverridesQuery, Dictionary<string, Dictionary<string, string>>>
{
    public async Task<Dictionary<string, Dictionary<string, string>>> Handle(GetActiveI18nLabelOverridesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.I18nLabelOverrides
            .AsNoTracking()
            .Where(o => !o.IsPendingTranslation)
            .Select(o => new { o.LanguageCode, o.Key, o.Value })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.LanguageCode)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.Key, r => r.Value));
    }
}
