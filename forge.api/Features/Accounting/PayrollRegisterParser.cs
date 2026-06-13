using System.Globalization;
using System.Text;

namespace Forge.Api.Features.Accounting;

/// <summary>One parsed per-employee register row.</summary>
public sealed record PayrollRegisterRow(
    string EmployeeName,
    decimal Gross,
    decimal Federal,
    decimal State,
    decimal FicaEmployee,
    decimal OtherDeductions,
    decimal EmployerTax,
    decimal Net);

/// <summary>
/// ⚡ PAY-001 — provider-agnostic payroll register parser (per-employee CSV). Column resolution
/// is two-tier:
/// <list type="number">
///   <item><b>Settings overrides</b> (payroll.register.column.* — comma-separated EXACT header
///         names, admin-editable) win when present.</item>
///   <item>Otherwise <b>synonym matching</b> against common register headers — and every header
///         matching a category SUMS into it, which is what makes split columns (e.g. separate
///         Social Security + Medicare) work without configuration.</item>
/// </list>
/// Amounts accept "$1,234.56" and "(123.45)" notations. Choosing a payroll provider is therefore
/// a mapping decision, never code.
/// </summary>
public static class PayrollRegisterParser
{
    /// <summary>Per-category column resolution: override list (exact headers) or null → synonyms.</summary>
    public sealed record ColumnOverrides(
        string? Employee = null,
        string? Gross = null,
        string? Federal = null,
        string? State = null,
        string? FicaEmployee = null,
        string? OtherDeductions = null,
        string? EmployerTax = null,
        string? Net = null);

    private static readonly (string Category, string[] Synonyms)[] AmountCategories =
    [
        ("gross", ["gross"]),
        ("federal", ["federal", "fed tax", "fed w/h", "fit"]),
        ("state", ["state", "sit", "local"]),
        ("ficaEmployee", ["fica", "social security", "oasdi", "medicare", "ss tax"]),
        ("otherDeductions", ["deduction", "401k", "401(k)", "benefit", "insurance", "garnish", "other withh"]),
        ("employerTax", ["employer", "er tax", "er fica", "company tax", "futa", "suta"]),
        ("net", ["net"]),
    ];

    private static readonly string[] EmployeeSynonyms = ["employee", "name", "worker"];

    public static IReadOnlyList<PayrollRegisterRow> Parse(string contents, ColumnOverrides? overrides = null)
    {
        var rows = ReadCsv(contents);
        if (rows.Count < 2)
            return [];

        var header = rows[0].Select(h => h.Trim()).ToList();
        var headerLower = header.Select(h => h.ToLowerInvariant()).ToList();

        // Per-category column index sets (a category may sum several columns).
        var columns = new Dictionary<string, List<int>>();
        foreach (var (category, synonyms) in AmountCategories)
            columns[category] = ResolveColumns(header, headerLower, OverrideFor(overrides, category), synonyms);

        var employeeColumns = ResolveColumns(header, headerLower, overrides?.Employee, EmployeeSynonyms);
        if (employeeColumns.Count == 0 || columns["gross"].Count == 0)
            throw new InvalidOperationException(
                "Register needs recognizable Employee and Gross columns (configure payroll.register.column.* "
                + "overrides in Admin → Settings → Payroll if the provider uses unusual headers).");

        // Employer-tax synonyms can collide with employee-side categories ("employer fica" contains
        // "fica") — employer wins; remove its columns from the employee-side sets.
        foreach (var (category, _) in AmountCategories.Where(c => c.Category != "employerTax"))
            columns[category] = columns[category].Except(columns["employerTax"]).ToList();

        var parsed = new List<PayrollRegisterRow>();
        foreach (var row in rows.Skip(1))
        {
            var employee = employeeColumns.Select(i => Cell(row, i)).FirstOrDefault(v => v.Length > 0);
            if (string.IsNullOrWhiteSpace(employee))
                continue; // blank rows
            // Register exports end with summary rows — never import them as an employee.
            var nameLower = employee.Trim().ToLowerInvariant();
            if (nameLower.StartsWith("total") || nameLower is "summary" or "grand total")
                continue;

            decimal Sum(string category) => columns[category].Sum(i => ParseAmount(Cell(row, i)));

            var gross = Sum("gross");
            if (gross == 0m && Sum("net") == 0m)
                continue; // separator/noise rows

            parsed.Add(new PayrollRegisterRow(
                employee.Trim(), gross, Sum("federal"), Sum("state"),
                Sum("ficaEmployee"), Sum("otherDeductions"), Sum("employerTax"), Sum("net")));
        }

        return parsed;
    }

    private static string? OverrideFor(ColumnOverrides? o, string category) => category switch
    {
        "gross" => o?.Gross,
        "federal" => o?.Federal,
        "state" => o?.State,
        "ficaEmployee" => o?.FicaEmployee,
        "otherDeductions" => o?.OtherDeductions,
        "employerTax" => o?.EmployerTax,
        "net" => o?.Net,
        _ => null,
    };

    private static List<int> ResolveColumns(
        List<string> header, List<string> headerLower, string? overrideCsv, string[] synonyms)
    {
        if (!string.IsNullOrWhiteSpace(overrideCsv))
        {
            var exact = overrideCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return header
                .Select((h, i) => (h, i))
                .Where(x => exact.Any(e => string.Equals(e, x.h, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.i)
                .ToList();
        }

        return headerLower
            .Select((h, i) => (h, i))
            .Where(x => synonyms.Any(s => x.h.Contains(s)))
            .Select(x => x.i)
            .ToList();
    }

    private static string Cell(List<string> row, int index)
        => index < row.Count ? row[index].Trim() : string.Empty;

    private static decimal ParseAmount(string text)
    {
        if (text.Length == 0)
            return 0m;
        var negative = text.StartsWith('(') && text.EndsWith(')');
        var cleaned = text.Trim('(', ')').Replace(",", string.Empty).Replace("$", string.Empty);
        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return 0m;
        return negative ? -value : value;
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
