using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.I18nLabelOverrides;

/// <summary>
/// Resolves the set of configured UI languages for translation fan-out.
/// Prefers the active <c>supported_languages</c> rows; falls back to the
/// shipped catalog languages (en/es) when none are configured.
/// </summary>
public static class I18nLanguages
{
    /// <summary>Languages the shipped UI catalogs cover (forge-ui public/assets/i18n/*.json).</summary>
    public static readonly IReadOnlyList<string> ShippedCodes = ["en", "es"];

    public static async Task<List<string>> GetConfiguredCodesAsync(AppDbContext db, CancellationToken ct)
    {
        var configured = await db.SupportedLanguages
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Code)
            .Select(l => l.Code)
            .ToListAsync(ct);

        return configured.Count > 0 ? configured : [.. ShippedCodes];
    }
}
