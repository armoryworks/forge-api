using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// The language a customer-facing document prints in: the requested language when it is one Forge ships document
/// text for, else the company's default language (the <c>supported_languages</c> row marked default), else English.
/// Region subtags are dropped ("es-US" prints Spanish).
/// </summary>
public static class DocumentLanguage
{
    public const string English = "en";
    public const string Spanish = "es";

    private static readonly string[] Shipped = [English, Spanish];

    public static async Task<string> ResolveAsync(AppDbContext db, string? requested, CancellationToken ct)
    {
        if (Normalize(requested) is { } explicitLanguage)
            return explicitLanguage;

        var companyDefault = await db.SupportedLanguages.AsNoTracking()
            .Where(l => l.IsDefault && l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => l.Code)
            .FirstOrDefaultAsync(ct);
        return Normalize(companyDefault) ?? English;
    }

    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        var primary = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return Shipped.Contains(primary) ? primary : null;
    }
}
