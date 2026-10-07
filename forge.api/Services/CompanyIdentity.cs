using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Resolves the install's company name for customer-facing documents and
/// emails. Reads <c>company.name</c> (written by Admin &gt; Company and initial
/// setup), falls back to the legacy <c>company_name</c> row, and returns null
/// when neither holds a non-blank value so callers decide how to degrade.
/// </summary>
public static class CompanyIdentity
{
    public const string NameKey = "company.name";
    public const string LegacyNameKey = "company_name";

    public static async Task<string?> GetCompanyNameAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == NameKey || s.Key == LegacyNameKey)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);

        return Pick(
            rows.FirstOrDefault(r => r.Key == NameKey)?.Value,
            rows.FirstOrDefault(r => r.Key == LegacyNameKey)?.Value);
    }

    public static async Task<string?> GetCompanyNameAsync(ISystemSettingRepository settings, CancellationToken ct)
    {
        var current = (await settings.FindByKeyAsync(NameKey, ct))?.Value;
        if (!string.IsNullOrWhiteSpace(current))
            return current.Trim();

        return Pick(current, (await settings.FindByKeyAsync(LegacyNameKey, ct))?.Value);
    }

    private static string? Pick(string? current, string? legacy)
    {
        if (!string.IsNullOrWhiteSpace(current)) return current.Trim();
        if (!string.IsNullOrWhiteSpace(legacy)) return legacy.Trim();
        return null;
    }
}
