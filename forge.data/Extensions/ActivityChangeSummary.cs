using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Forge.Core.Entities;

namespace Forge.Data.Extensions;

/// <summary>
/// Turns property and entity-type names into the human wording shown on the
/// Activity and History tabs, and finds which properties of a tracked entity
/// really changed. Shared by the <see cref="Context.AppDbContext"/> audit
/// interceptor and the rollup descriptions written by update handlers, so a
/// field reads the same everywhere ("credit limit", "QC inspection",
/// "stock UoM ID") instead of leaking camelCase DTO or PascalCase CLR names.
/// </summary>
public static class ActivityChangeSummary
{
    private static readonly Dictionary<string, string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abc"] = "ABC",
        ["api"] = "API",
        ["bom"] = "BOM",
        ["capa"] = "CAPA",
        ["csv"] = "CSV",
        ["eco"] = "ECO",
        ["ein"] = "EIN",
        ["fai"] = "FAI",
        ["fda"] = "FDA",
        ["fx"] = "FX",
        ["gl"] = "GL",
        ["hts"] = "HTS",
        ["icp"] = "ICP",
        ["id"] = "ID",
        ["itar"] = "ITAR",
        ["kpi"] = "KPI",
        ["mfa"] = "MFA",
        ["mpn"] = "MPN",
        ["mrp"] = "MRP",
        ["nda"] = "NDA",
        ["ncr"] = "NCR",
        ["oee"] = "OEE",
        ["ooc"] = "OOC",
        ["pdf"] = "PDF",
        ["po"] = "PO",
        ["ppap"] = "PPAP",
        ["qc"] = "QC",
        ["rfq"] = "RFQ",
        ["scac"] = "SCAC",
        ["sku"] = "SKU",
        ["sms"] = "SMS",
        ["spc"] = "SPC",
        ["uom"] = "UoM",
        ["upc"] = "UPC",
        ["url"] = "URL",
        ["wip"] = "WIP",
    };

    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
    {
        nameof(BaseAuditableEntity.CreatedAt),
        nameof(BaseAuditableEntity.UpdatedAt),
        nameof(BaseAuditableEntity.DeletedAt),
        nameof(BaseAuditableEntity.DeletedBy),
        nameof(BaseEntity.Id),
        nameof(IConcurrencyVersioned.Version),
    };

    /// <summary>
    /// Lower-case label for a property name, for use inside a sentence:
    /// <c>creditLimit</c> → "credit limit", <c>IsFdaRegulated</c> → "FDA regulated",
    /// <c>StockUomId</c> → "stock UoM ID", <c>set-default</c> → "set default".
    /// </summary>
    public static string Label(string name) => Compose(name, titleCase: false);

    /// <summary>
    /// Sentence-case label for a value shown on its own: <c>InProgress</c> → "In progress",
    /// <c>QcInspection</c> → "QC inspection".
    /// </summary>
    public static string SentenceLabel(string name)
    {
        var label = Label(name);
        return label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];
    }

    /// <summary>
    /// Title-case label for an entity type or a property used as a heading:
    /// <c>QcInspection</c> → "QC Inspection", <c>CustomerPO</c> → "Customer PO".
    /// </summary>
    public static string TitleLabel(string name) => Compose(name, titleCase: true);

    /// <summary>
    /// Humanizes one rollup entry. Plain names go through <see cref="Label"/>; an entry of the
    /// form <c>name: value</c> keeps its value (booleans become yes/no), and an entry with a
    /// trailing note such as <c>items (1 added)</c> keeps the note.
    /// </summary>
    public static string Entry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return entry;

        var colon = entry.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && IsIdentifier(entry[..colon]))
        {
            var value = entry[(colon + 2)..];
            if (bool.TryParse(value, out var flag)) value = flag ? "yes" : "no";
            return $"{Label(entry[..colon])}: {value}";
        }

        var paren = entry.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && IsIdentifier(entry[..paren]))
            return Label(entry[..paren]) + entry[paren..];

        return IsIdentifier(entry) ? Label(entry) : entry;
    }

    /// <summary>
    /// Joins rollup entries for an activity description, each humanized by <see cref="Entry"/>.
    /// </summary>
    public static string Fields(IEnumerable<string> entries) => string.Join(", ", entries.Select(Entry));

    /// <summary>
    /// Builds the standard rollup description, "Updated credit limit" for one field or
    /// "Updated 3 fields: name, description, status" for several. Returns <c>null</c> when
    /// nothing changed so the caller can skip the activity row.
    /// </summary>
    public static string? Updated(IReadOnlyCollection<string> entries) => entries.Count switch
    {
        0 => null,
        1 => $"Updated {Entry(entries.First())}",
        _ => $"Updated {entries.Count} fields: {Fields(entries)}",
    };

    /// <summary>
    /// Names of the scalar properties of a tracked entity whose current value differs from
    /// its original value, skipping audit plumbing (timestamps, soft-delete markers, Id,
    /// concurrency version) and shadow properties. Call after
    /// <see cref="ChangeTracker.DetectChanges"/>; an untracked entity reports nothing.
    /// </summary>
    public static IReadOnlyList<string> ChangedProperties(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified) return [];

        var names = new List<string>();
        foreach (var prop in entry.Properties)
        {
            if (!prop.IsModified) continue;
            if (ExcludedProperties.Contains(prop.Metadata.Name)) continue;
            if (prop.Metadata.IsShadowProperty()) continue;
            if (FormatValue(prop.OriginalValue) == FormatValue(prop.CurrentValue)) continue;
            names.Add(prop.Metadata.Name);
        }
        return names;
    }

    /// <summary>
    /// Formats a property value the way activity history stores it, so "changed" means the
    /// same thing to the interceptor and to <see cref="ChangedProperties"/>.
    /// </summary>
    public static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal d => d.ToString("G", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "",
    };

    private static bool IsIdentifier(string s) =>
        s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

    private static string Compose(string name, bool titleCase)
    {
        var words = Split(name);
        if (words.Count > 1 && string.Equals(words[0], "is", StringComparison.OrdinalIgnoreCase))
            words.RemoveAt(0);

        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (Acronyms.TryGetValue(word, out var acronym))
                sb.Append(acronym);
            else if (titleCase)
                sb.Append(char.ToUpperInvariant(word[0])).Append(word[1..].ToLowerInvariant());
            else
                sb.Append(word.ToLowerInvariant());
        }
        return sb.ToString();
    }

    private static List<string> Split(string name)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0) words.Add(current.ToString());
            current.Clear();
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '-' or '_' or ' ')
            {
                Flush();
                continue;
            }
            if (current.Length > 0)
            {
                var prev = name[i - 1];
                var boundary =
                    (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev)))
                    || (char.IsUpper(c) && char.IsUpper(prev) && i + 1 < name.Length && char.IsLower(name[i + 1]))
                    || (char.IsDigit(c) && char.IsLetter(prev))
                    || (char.IsLetter(c) && char.IsDigit(prev));
                if (boundary) Flush();
            }
            current.Append(c);
        }
        Flush();
        return words;
    }
}
