using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Api.Features.Accounting;

/// <summary>One parsed statement transaction (format-agnostic).</summary>
public sealed record ParsedStatementLine(
    DateOnly PostedDate,
    decimal Amount,        // signed: + deposit / − withdrawal
    string Description,
    string Fitid);

/// <summary>
/// ⚡ BANK-001 — lenient parsers for the two manual statement formats:
/// <list type="bullet">
///   <item><b>OFX</b> — both 1.x (SGML, unclosed tags) and 2.x (XML). Extraction is regex-per-tag
///         inside each <c>&lt;STMTTRN&gt;</c> block, which tolerates either dialect; the bank's
///         FITID is the dedupe key verbatim.</item>
///   <item><b>CSV</b> — header-mapped Date/Amount/Description columns (case-insensitive, with
///         common synonyms). CSV has no FITID, so the dedupe key is a content hash of
///         date|amount|description|occurrence-index (the index disambiguates true same-day
///         duplicates like two identical card charges).</item>
/// </list>
/// Pure + deterministic: no clock, no I/O.
/// </summary>
public static partial class BankStatementParser
{
    [GeneratedRegex(@"<STMTTRN>(.*?)(?:</STMTTRN>|(?=<STMTTRN>)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StmtTrnBlock();

    [GeneratedRegex(@"<DTPOSTED>\s*([0-9]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex DtPosted();

    [GeneratedRegex(@"<TRNAMT>\s*([-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex TrnAmt();

    [GeneratedRegex(@"<FITID>\s*([^<\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FitId();

    [GeneratedRegex(@"<(?:NAME|MEMO)>\s*([^<\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NameOrMemo();

    public static bool LooksLikeOfx(string contents)
        => contents.Contains("<OFX", StringComparison.OrdinalIgnoreCase)
            || contents.Contains("OFXHEADER", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ParsedStatementLine> ParseOfx(string contents)
    {
        var lines = new List<ParsedStatementLine>();

        foreach (Match block in StmtTrnBlock().Matches(contents))
        {
            var body = block.Groups[1].Value;

            var dateMatch = DtPosted().Match(body);
            var amountMatch = TrnAmt().Match(body);
            var fitidMatch = FitId().Match(body);
            if (!dateMatch.Success || !amountMatch.Success || !fitidMatch.Success)
                continue; // tolerate malformed blocks rather than failing the whole import

            var date = DateOnly.ParseExact(dateMatch.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture);
            var amount = decimal.Parse(amountMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var description = NameOrMemo().Match(body) is { Success: true } nm
                ? nm.Groups[1].Value.Trim()
                : string.Empty;

            lines.Add(new ParsedStatementLine(date, amount, description, fitidMatch.Groups[1].Value.Trim()));
        }

        return lines;
    }

    /// <summary>
    /// Header-mapped CSV. Recognized headers (case-insensitive): date | posted | posting date;
    /// amount; description | memo | payee | name. Amounts accept "(1,234.56)" negative notation.
    /// </summary>
    public static IReadOnlyList<ParsedStatementLine> ParseCsv(string contents)
    {
        var rows = ReadCsv(contents);
        if (rows.Count < 2)
            return [];

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Any(n => h.Contains(n)));

        var dateCol = Col("date", "posted");
        var amountCol = Col("amount");
        var descCol = Col("description", "memo", "payee", "name");
        if (dateCol < 0 || amountCol < 0)
            throw new InvalidOperationException(
                "CSV statement needs recognizable Date and Amount columns (header row required).");

        var lines = new List<ParsedStatementLine>();
        var occurrence = new Dictionary<string, int>();

        foreach (var row in rows.Skip(1))
        {
            if (row.Count <= Math.Max(dateCol, amountCol))
                continue;

            var dateText = row[dateCol].Trim();
            var amountText = row[amountCol].Trim();
            if (dateText.Length == 0 || amountText.Length == 0)
                continue;

            if (!TryParseDate(dateText, out var date))
                continue;

            var negativeParens = amountText.StartsWith('(') && amountText.EndsWith(')');
            var cleaned = amountText.Trim('(', ')').Replace(",", string.Empty).Replace("$", string.Empty);
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                continue;
            if (negativeParens)
                amount = -amount;

            var description = descCol >= 0 && row.Count > descCol ? row[descCol].Trim() : string.Empty;

            // Content hash + occurrence index = stable dedupe key without a bank FITID.
            var basis = $"{date:yyyy-MM-dd}|{amount}|{description}";
            occurrence[basis] = occurrence.TryGetValue(basis, out var n) ? n + 1 : 0;
            var fitid = "CSV-" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{basis}|{occurrence[basis]}")))[..24];

            lines.Add(new ParsedStatementLine(date, amount, description, fitid));
        }

        return lines;
    }

    private static bool TryParseDate(string text, out DateOnly date)
    {
        foreach (var format in new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "MM-dd-yyyy", "yyyyMMdd" })
        {
            if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, out date);
    }

    /// <summary>Minimal RFC-4180 reader (quoted fields, escaped quotes, CRLF/LF).</summary>
    private static List<List<string>> ReadCsv(string contents)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < contents.Length; i++)
        {
            var c = contents[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < contents.Length && contents[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < contents.Length && contents[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0))
                    rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }

        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0))
            rows.Add(row);

        return rows;
    }
}
