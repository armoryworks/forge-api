using System.Text.RegularExpressions;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// Default implementation of <see cref="IFolderPathResolver"/>. Substitutes
/// <c>{Token}</c> placeholders in a path template with values from the
/// caller's context dictionary. Falls back to standard date tokens
/// (<c>{Year}</c> / <c>{Month}</c> / <c>{Quarter}</c>) using
/// <c>DateTimeOffset.UtcNow</c> when not present in the context.
/// </summary>
public class FolderPathResolver : IFolderPathResolver
{
    // Matches {Token} or {token_name} — captures the inner name.
    private static readonly Regex TokenPattern = new(
        @"\{(?<name>[A-Za-z][A-Za-z0-9_]*)\}",
        RegexOptions.Compiled);

    public string Resolve(string template, IReadOnlyDictionary<string, string>? context = null)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var ctx = context is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase);

        // Standard date tokens — fill from now when caller didn't provide them.
        var now = DateTimeOffset.UtcNow;
        if (!ctx.ContainsKey("Year")) ctx["Year"] = now.Year.ToString();
        if (!ctx.ContainsKey("Month")) ctx["Month"] = now.Month.ToString("D2");
        if (!ctx.ContainsKey("Quarter")) ctx["Quarter"] = $"Q{(now.Month - 1) / 3 + 1}";

        return TokenPattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return ctx.TryGetValue(name, out var value)
                ? SanitizeValue(value)
                : match.Value;  // leave unmatched tokens literal
        });
    }

    /// <summary>
    /// Sanitize a token value for use in a folder-path segment. Slashes
    /// and backslashes become dashes so a value like <c>"ACME / Inc"</c>
    /// doesn't accidentally split into multiple path segments. Trims
    /// whitespace; collapses internal whitespace runs to single spaces.
    /// </summary>
    private static string SanitizeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = value
            .Replace('/', '-')
            .Replace('\\', '-')
            .Trim();
        return Regex.Replace(sanitized, @"\s+", " ");
    }
}
