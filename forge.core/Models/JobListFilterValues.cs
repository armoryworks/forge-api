using System.Globalization;

using Forge.Core.Enums;

namespace Forge.Core.Models;

/// <summary>Value readers for <see cref="JobListFilter"/>; each returns null when the value is malformed.</summary>
public static class JobListFilterValues
{
    /// <summary>The non-blank, trimmed <c>|</c>-separated entries of an <c>in</c> value.</summary>
    public static IReadOnlyList<string> List(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The billing statuses a job list reports for a completed job.</summary>
    public static readonly IReadOnlyList<string> BillingStatuses = ["Invoiced", "Uninvoiced"];

    /// <summary>
    /// True when every entry of an enum field's <c>in</c> value is acceptable: ids for the id fields, enum
    /// names (any case) for priority, disposition and billing status.
    /// </summary>
    public static bool EnumValuesFit(string key, string value)
    {
        var entries = List(value);
        if (entries.Count == 0) return false;
        return key switch
        {
            "priorityName" => entries.All(e => Enum.TryParse<JobPriority>(e, true, out _) && !int.TryParse(e, out _)),
            "disposition" => entries.All(e => Enum.TryParse<JobDisposition>(e, true, out _) && !int.TryParse(e, out _)),
            "billingStatus" => entries.All(e => BillingStatuses.Contains(e, StringComparer.OrdinalIgnoreCase)),
            _ => Ids(value) is not null,
        };
    }

    /// <summary>The integer entries of an <c>in</c> value, or null when any entry is not an integer.</summary>
    public static IReadOnlyList<int>? Ids(string value)
    {
        var ids = new List<int>();
        foreach (var entry in List(value))
        {
            if (!int.TryParse(entry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return null;
            ids.Add(id);
        }
        return ids.Count > 0 ? ids : null;
    }

    /// <summary>The boolean entries of an <c>in</c> value, or null when any entry is not true/false.</summary>
    public static IReadOnlyList<bool>? Booleans(string value)
    {
        var flags = new List<bool>();
        foreach (var entry in List(value))
        {
            if (!bool.TryParse(entry, out var flag)) return null;
            flags.Add(flag);
        }
        return flags.Count > 0 ? flags : null;
    }

    /// <summary>The bounds of a number <c>min..max</c> value; at least one side must be present.</summary>
    public static (decimal? Min, decimal? Max)? NumberRange(string value)
    {
        if (!SplitRange(value, out var low, out var high)) return null;
        decimal? min = null, max = null;
        if (low.Length > 0)
        {
            if (!decimal.TryParse(low, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return null;
            min = parsed;
        }
        if (high.Length > 0)
        {
            if (!decimal.TryParse(high, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return null;
            max = parsed;
        }
        return (min, max);
    }

    /// <summary>
    /// The bounds of a date <c>from..to</c> value as a half-open UTC interval: <c>From</c> is the start of
    /// the first day and <c>ToExclusive</c> the start of the day after the last.
    /// </summary>
    public static (DateTimeOffset? From, DateTimeOffset? ToExclusive)? DateRange(string value)
    {
        if (!SplitRange(value, out var low, out var high)) return null;
        DateTimeOffset? from = null, to = null;
        if (low.Length > 0)
        {
            if (!TryParseDay(low, out var day)) return null;
            from = day;
        }
        if (high.Length > 0)
        {
            if (!TryParseDay(high, out var day)) return null;
            to = day.AddDays(1);
        }
        return (from, to);
    }

    private static bool SplitRange(string value, out string low, out string high)
    {
        var at = value.IndexOf("..", StringComparison.Ordinal);
        low = high = string.Empty;
        if (at < 0) return false;
        low = value[..at].Trim();
        high = value[(at + 2)..].Trim();
        return low.Length > 0 || high.Length > 0;
    }

    private static bool TryParseDay(string text, out DateTimeOffset day)
    {
        day = default;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;
        day = new DateTimeOffset(parsed.UtcDateTime.Date, TimeSpan.Zero);
        return true;
    }
}
