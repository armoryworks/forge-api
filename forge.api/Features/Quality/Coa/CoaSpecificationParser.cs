using System.Globalization;
using System.Text.RegularExpressions;

namespace Forge.Api.Features.Quality.Coa;

/// <summary>
/// Reads numeric limits out of a free-text inspection specification so a certificate can print them as
/// min / max / target. Understands "10 ± 0.1" (also "+/-"), "9.9 - 10.1" (also "to" and an en dash),
/// "&lt;= 5" / "≤ 5" / "max 5" and "&gt;= 2" / "≥ 2" / "min 2". Anything else keeps only its text.
/// </summary>
public static partial class CoaSpecificationParser
{
    public static (decimal? Min, decimal? Max, decimal? Target) Parse(string? specification)
    {
        if (string.IsNullOrWhiteSpace(specification))
            return (null, null, null);

        var text = specification.Trim();

        var tolerance = Tolerance().Match(text);
        if (tolerance.Success)
        {
            var target = Number(tolerance.Groups["target"].Value);
            var tol = Number(tolerance.Groups["tol"].Value);
            return (target - tol, target + tol, target);
        }

        var range = Range().Match(text);
        if (range.Success)
        {
            var a = Number(range.Groups["a"].Value);
            var b = Number(range.Groups["b"].Value);
            return (Math.Min(a, b), Math.Max(a, b), null);
        }

        var max = Maximum().Match(text);
        if (max.Success)
            return (null, Number(max.Groups["v"].Value), null);

        var min = Minimum().Match(text);
        if (min.Success)
            return (Number(min.Groups["v"].Value), null, null);

        return (null, null, null);
    }

    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<target>-?\d+(?:\.\d+)?)\s*(?:±|\+/-|\+-)\s*(?<tol>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex Tolerance();

    [GeneratedRegex(@"^(?<a>-?\d+(?:\.\d+)?)\s*(?:-|–|to)\s*(?<b>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Range();

    [GeneratedRegex(@"^(?:<=|≤|<|max\.?|maximum)\s*(?<v>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Maximum();

    [GeneratedRegex(@"^(?:>=|≥|>|min\.?|minimum)\s*(?<v>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Minimum();
}
