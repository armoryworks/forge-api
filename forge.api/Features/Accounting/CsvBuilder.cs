using System.Globalization;
using System.Text;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Tiny shared CSV writer for the QB-001 CPA exports (§5 Phase-4 / §10 ratification:
/// "CSV/Excel export always available"). RFC 4180 quoting: a field containing a
/// comma, double-quote, CR or LF is wrapped in double-quotes with embedded quotes
/// doubled. Amounts are invariant-culture 2dp (no currency symbol); dates are ISO
/// <c>yyyy-MM-dd</c>. Shared by the three export handlers so the dialect can never
/// drift between files the CPA loads side by side.
/// </summary>
public sealed class CsvBuilder
{
    private readonly StringBuilder _sb = new();

    /// <summary>Append one CSV record (RFC 4180 CRLF line ending).</summary>
    public CsvBuilder AppendRow(params string?[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) _sb.Append(',');
            _sb.Append(Escape(fields[i]));
        }

        _sb.Append("\r\n");
        return this;
    }

    /// <summary>Invariant-culture amount, 2 decimal places, no currency symbol.</summary>
    public static string Amount(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>ISO date (<c>yyyy-MM-dd</c>).</summary>
    public static string Date(DateOnly value)
        => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Filename-friendly period suffix: a single calendar month renders as
    /// <c>yyyy-MM</c> (e.g. <c>trial-balance-2026-06.csv</c>), an arbitrary range as
    /// <c>from_to</c>, an open end as <c>start</c>/<c>end</c>, no range at all as
    /// <c>all</c>.
    /// </summary>
    public static string RangeSuffix(DateOnly? fromDate, DateOnly? toDate)
    {
        if (fromDate is null && toDate is null) return "all";

        if (fromDate is { } from && toDate is { } to
            && from.Year == to.Year && from.Month == to.Month)
        {
            return from.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        var fromPart = fromDate is { } f ? Date(f) : "start";
        var toPart = toDate is { } t ? Date(t) : "end";
        return $"{fromPart}_{toPart}";
    }

    public byte[] ToUtf8Bytes() => Encoding.UTF8.GetBytes(_sb.ToString());

    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;

        var needsQuoting = field.Contains(',') || field.Contains('"')
            || field.Contains('\r') || field.Contains('\n');

        return needsQuoting ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
