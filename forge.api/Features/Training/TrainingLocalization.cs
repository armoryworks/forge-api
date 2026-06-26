using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Training;

/// <summary>
/// Resolves per-locale training content. English is the canonical base stored on the module/path
/// rows themselves; for any other locale the API overlays a row from
/// training_module_translations / training_path_translations when present, falling back to English.
/// </summary>
public static class TrainingLocalization
{
    /// <summary>English (or empty) → null (no overlay). Otherwise the language subtag, e.g. "es-MX" → "es".</summary>
    public static string? Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return null;
        var l = lang.Trim().ToLowerInvariant();
        if (l == "en" || l.StartsWith("en-")) return null;
        var dash = l.IndexOf('-');
        return dash > 0 ? l[..dash] : l;
    }

    public static async Task<Dictionary<int, TrainingModuleTranslation>> ModuleTranslationsAsync(
        AppDbContext db, IReadOnlyCollection<int> moduleIds, string? lang, CancellationToken ct)
    {
        var locale = Normalize(lang);
        if (locale is null || moduleIds.Count == 0) return [];
        return await db.TrainingModuleTranslations.AsNoTracking()
            .Where(t => t.Locale == locale && moduleIds.Contains(t.TrainingModuleId))
            .ToDictionaryAsync(t => t.TrainingModuleId, ct);
    }

    public static async Task<Dictionary<int, TrainingPathTranslation>> PathTranslationsAsync(
        AppDbContext db, IReadOnlyCollection<int> pathIds, string? lang, CancellationToken ct)
    {
        var locale = Normalize(lang);
        if (locale is null || pathIds.Count == 0) return [];
        return await db.TrainingPathTranslations.AsNoTracking()
            .Where(t => t.Locale == locale && pathIds.Contains(t.TrainingPathId))
            .ToDictionaryAsync(t => t.TrainingPathId, ct);
    }
}
