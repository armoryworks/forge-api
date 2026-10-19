using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Eco;

/// <summary>Resolves display labels for ECO affected items (a part's number and name, an operation's part and step) in place of their database ids.</summary>
public static class EcoAffectedItemLabels
{
    public static async Task<Dictionary<(string EntityType, int EntityId), string>> ResolveAsync(
        AppDbContext db, IEnumerable<EcoAffectedItem> items, CancellationToken ct)
    {
        var list = items.ToList();
        var labels = new Dictionary<(string, int), string>();

        var partIds = IdsOf(list, "Part");
        if (partIds.Count > 0)
        {
            var parts = await db.Parts.AsNoTracking()
                .Where(p => partIds.Contains(p.Id))
                .Select(p => new { p.Id, p.PartNumber, p.Name })
                .ToListAsync(ct);
            foreach (var p in parts)
                labels[("Part", p.Id)] = $"{p.PartNumber} — {p.Name}";
        }

        var operationIds = IdsOf(list, "Operation");
        if (operationIds.Count > 0)
        {
            var operations = await db.Operations.AsNoTracking()
                .Where(o => operationIds.Contains(o.Id))
                .Select(o => new { o.Id, o.StepNumber, o.Title, o.Part.PartNumber })
                .ToListAsync(ct);
            foreach (var o in operations)
                labels[("Operation", o.Id)] = $"{o.PartNumber} op {o.StepNumber} — {o.Title}";
        }

        return list
            .Where(i => labels.ContainsKey((Normalize(i.EntityType), i.EntityId)))
            .GroupBy(i => (i.EntityType, i.EntityId))
            .ToDictionary(g => g.Key, g => labels[(Normalize(g.Key.EntityType), g.Key.EntityId)]);
    }

    private static List<int> IdsOf(List<EcoAffectedItem> items, string entityType) => items
        .Where(i => Normalize(i.EntityType) == entityType)
        .Select(i => i.EntityId)
        .Distinct()
        .ToList();

    private static string Normalize(string entityType) =>
        string.Equals(entityType, "Part", StringComparison.OrdinalIgnoreCase) ? "Part"
        : string.Equals(entityType, "Operation", StringComparison.OrdinalIgnoreCase) ? "Operation"
        : entityType;
}
