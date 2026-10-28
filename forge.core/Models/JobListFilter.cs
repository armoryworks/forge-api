namespace Forge.Core.Models;

/// <summary>
/// One parsed <c>filter=</c> value of <c>GET /api/v1/jobs</c>, written <c>field:operator:value</c>.
/// Text fields take <c>contains</c>, <c>equals</c>, <c>startsWith</c>, <c>endsWith</c>, <c>notContains</c>
/// or <c>notEquals</c> (case-insensitive). Enum and boolean fields take <c>in</c> with <c>|</c>-separated
/// values. Number and date fields take <c>range</c> with <c>min..max</c>, either side optional; a date
/// range covers whole UTC calendar days, so <c>..2026-03-31</c> includes the 31st.
/// </summary>
/// <param name="Field">The whitelisted field.</param>
/// <param name="Operator">The operator, lower-cased.</param>
/// <param name="Value">The raw value after the second colon.</param>
public record JobListFilter(JobListField Field, string Operator, string Value)
{
    public static readonly IReadOnlySet<string> TextOperators =
        new HashSet<string>(["contains", "equals", "startswith", "endswith", "notcontains", "notequals"]);

    /// <summary>
    /// Parses <paramref name="raw"/>. Returns null and an <paramref name="error"/> when the shape, field,
    /// operator or value is not acceptable. Field access is the caller's concern.
    /// </summary>
    public static JobListFilter? Parse(string? raw, out string? error)
    {
        error = null;
        var parts = (raw ?? string.Empty).Split(':', 3);
        if (parts.Length < 3 || parts[0].Trim().Length == 0)
        {
            error = $"Filter '{raw}' must be written field:operator:value.";
            return null;
        }

        var field = JobListFields.Find(parts[0]);
        if (field is null || !field.Filterable)
        {
            error = $"'{parts[0]}' is not a filterable job field.";
            return null;
        }

        var op = parts[1].Trim().ToLowerInvariant();
        var value = parts[2];
        var accepted = field.Kind switch
        {
            JobListFieldKind.Text => TextOperators.Contains(op) && value.Trim().Length > 0,
            JobListFieldKind.Enum => op == "in" && JobListFilterValues.EnumValuesFit(field.Key, value),
            JobListFieldKind.Boolean => op == "in" && JobListFilterValues.Booleans(value) is { Count: > 0 },
            JobListFieldKind.Number => op == "range" && JobListFilterValues.NumberRange(value) is not null,
            JobListFieldKind.Date => op == "range" && JobListFilterValues.DateRange(value) is not null,
            _ => false,
        };
        if (!accepted)
        {
            error = $"Filter '{raw}' has an operator or value that does not fit {field.Key}.";
            return null;
        }

        return new JobListFilter(field, op, value);
    }
}
