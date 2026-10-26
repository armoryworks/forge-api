using System.Collections.Frozen;

namespace Forge.Core.Constants;

/// <summary>
/// Compares free-text address countries. Addresses store whatever the user
/// typed ("US", "USA", "United States"), so two addresses are in the same
/// country when their normalized forms match.
/// </summary>
public static class CountryCodes
{
    private static readonly FrozenDictionary<string, string> Aliases = new Dictionary<string, string>
    {
        ["USA"] = "US",
        ["U.S."] = "US",
        ["U.S.A."] = "US",
        ["UNITED STATES"] = "US",
        ["UNITED STATES OF AMERICA"] = "US",
        ["CAN"] = "CA",
        ["CANADA"] = "CA",
        ["MEX"] = "MX",
        ["MEXICO"] = "MX",
        ["GBR"] = "GB",
        ["UK"] = "GB",
        ["UNITED KINGDOM"] = "GB",
        ["DEU"] = "DE",
        ["GERMANY"] = "DE",
        ["CHN"] = "CN",
        ["CHINA"] = "CN",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The upper-case two-letter code for a known alias, otherwise the trimmed
    /// upper-case input; empty when the input is blank.
    /// </summary>
    public static string Normalize(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return string.Empty;
        var key = country.Trim().ToUpperInvariant();
        return Aliases.TryGetValue(key, out var code) ? code : key;
    }

    /// <summary>
    /// True when both countries are present and normalize to different codes.
    /// A blank country is treated as domestic.
    /// </summary>
    public static bool AreDifferent(string? a, string? b)
    {
        var left = Normalize(a);
        var right = Normalize(b);
        return left.Length > 0 && right.Length > 0 && left != right;
    }
}
