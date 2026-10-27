using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Imports;

/// <summary>
/// Resolves the customer and part names a CSV import carries to ids, matching the business number first
/// and then the name, case-insensitively. A name shared by more than one record is reported as ambiguous
/// rather than guessed.
/// </summary>
internal sealed class ImportReferenceLookup
{
    private readonly Dictionary<string, List<(int Id, string? Number)>> _customers;
    private readonly Dictionary<string, List<(int Id, string Number)>> _parts;

    private ImportReferenceLookup(
        Dictionary<string, List<(int, string?)>> customers, Dictionary<string, List<(int, string)>> parts)
    {
        _customers = customers;
        _parts = parts;
    }

    public static async Task<ImportReferenceLookup> LoadAsync(
        AppDbContext db, IEnumerable<string?> customerKeys, IEnumerable<string?> partKeys, CancellationToken ct)
    {
        var cKeys = Normalize(customerKeys);
        var pKeys = Normalize(partKeys);

        var customers = cKeys.Count == 0
            ? []
            : await db.Customers.AsNoTracking()
                .Where(c => (c.CustomerNumber != null && cKeys.Contains(c.CustomerNumber.ToLower()))
                    || cKeys.Contains(c.Name.ToLower())
                    || (c.CompanyName != null && cKeys.Contains(c.CompanyName.ToLower())))
                .Select(c => new { c.Id, c.CustomerNumber, c.Name, c.CompanyName })
                .ToListAsync(ct);
        var parts = pKeys.Count == 0
            ? []
            : await db.Parts.AsNoTracking()
                .Where(p => pKeys.Contains(p.PartNumber.ToLower()) || pKeys.Contains(p.Name.ToLower()))
                .Select(p => new { p.Id, p.PartNumber, p.Name })
                .ToListAsync(ct);

        var customerIndex = new Dictionary<string, List<(int, string?)>>();
        foreach (var c in customers)
        {
            if (c.CustomerNumber is { } number && cKeys.Contains(number.ToLowerInvariant()))
                Add(customerIndex, "#" + number.ToLowerInvariant(), (c.Id, c.CustomerNumber));
            foreach (var name in new[] { c.Name, c.CompanyName }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
                Add(customerIndex, name!.ToLowerInvariant(), (c.Id, c.CustomerNumber));
        }
        var partIndex = new Dictionary<string, List<(int, string)>>();
        foreach (var p in parts)
        {
            Add(partIndex, "#" + p.PartNumber.ToLowerInvariant(), (p.Id, p.PartNumber));
            Add(partIndex, p.Name.ToLowerInvariant(), (p.Id, p.PartNumber));
        }
        return new ImportReferenceLookup(customerIndex, partIndex);
    }

    public int? Customer(string? key, List<string> errors)
        => Resolve(_customers, key, "customer", errors);

    public int? Part(string? key, List<string> errors)
        => Resolve(_parts, key, "part", errors);

    private static int? Resolve<T>(Dictionary<string, List<(int Id, T Number)>> index, string? key, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var k = key.Trim().ToLowerInvariant();
        if (index.TryGetValue("#" + k, out var byNumber) && byNumber.Count > 0)
            return byNumber[0].Id;
        if (index.TryGetValue(k, out var byName))
        {
            var ids = byName.Select(x => x.Id).Distinct().ToList();
            if (ids.Count == 1) return ids[0];
            errors.Add($"More than one {what} is named '{key.Trim()}'. Use the {what} number instead.");
            return null;
        }
        errors.Add($"Unknown {what} '{key.Trim()}'.");
        return null;
    }

    private static void Add<T>(Dictionary<string, List<T>> index, string key, T value)
    {
        if (!index.TryGetValue(key, out var list)) index[key] = list = [];
        list.Add(value);
    }

    private static List<string> Normalize(IEnumerable<string?> keys) =>
        keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim().ToLowerInvariant()).Distinct().ToList();
}
