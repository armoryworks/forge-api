using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

/// <summary>
/// A part that may be issued to a job building <see cref="ParentPartId"/>, with the quantity it takes per unit built.
/// <see cref="AlternateForPartId"/> names the BOM component it stands in for when it is an approved alternate.
/// </summary>
public record IssuableComponent(int ParentPartId, int IssuedPartId, decimal PerUnit, int? AlternateForPartId);

/// <summary>
/// Which parts a job may take as material: every BOM component of the job's part, and each approved alternate of a
/// component. An alternate row lets its alternate part stand in for its primary part, scaled by the conversion factor;
/// the primary stands in for the alternate only when the row is bidirectional, and then the factor divides.
/// </summary>
public static class JobMaterialRules
{
    public static Task<List<IssuableComponent>> ForParentAsync(AppDbContext db, int parentPartId, CancellationToken ct)
        => IssuableAsync(db, db.BOMLines.Where(b => b.ParentPartId == parentPartId), ct);

    public static async Task<List<IssuableComponent>> ForIssuedPartAsync(AppDbContext db, int issuedPartId, CancellationToken ct)
    {
        var alternateRows = await db.PartAlternates.AsNoTracking()
            .Where(a => a.IsApproved && (a.AlternatePartId == issuedPartId || (a.IsBidirectional && a.PartId == issuedPartId)))
            .Select(a => new { a.PartId, a.AlternatePartId })
            .ToListAsync(ct);
        var standsInFor = alternateRows
            .Select(a => a.AlternatePartId == issuedPartId ? a.PartId : a.AlternatePartId)
            .Append(issuedPartId)
            .Distinct()
            .ToList();
        var components = await IssuableAsync(db, db.BOMLines.Where(b => standsInFor.Contains(b.ChildPartId)), ct);
        return components.Where(c => c.IssuedPartId == issuedPartId).ToList();
    }

    private static async Task<List<IssuableComponent>> IssuableAsync(
        AppDbContext db, IQueryable<Core.Entities.BOMLine> lines, CancellationToken ct)
    {
        var bom = await lines.AsNoTracking()
            .Select(b => new { b.ParentPartId, b.ChildPartId, b.Quantity })
            .ToListAsync(ct);
        var childIds = bom.Select(b => b.ChildPartId).Distinct().ToList();
        var alternates = childIds.Count == 0
            ? []
            : await db.PartAlternates.AsNoTracking()
                .Where(a => a.IsApproved
                    && (childIds.Contains(a.PartId) || (a.IsBidirectional && childIds.Contains(a.AlternatePartId))))
                .OrderBy(a => a.Priority)
                .ThenBy(a => a.Id)
                .Select(a => new { a.PartId, a.AlternatePartId, a.ConversionFactor, a.IsBidirectional })
                .ToListAsync(ct);

        var result = bom
            .Select(b => new IssuableComponent(b.ParentPartId, b.ChildPartId, b.Quantity, null))
            .ToList();
        var primaries = result.Select(c => (c.ParentPartId, c.IssuedPartId)).ToHashSet();
        var alternateAt = new Dictionary<(int Parent, int Issued), int>();

        foreach (var line in bom)
        {
            var substitutes = alternates
                .Where(a => a.PartId == line.ChildPartId)
                .Select(a => (Part: a.AlternatePartId, PerUnit: line.Quantity * (a.ConversionFactor ?? 1m)))
                .Concat(alternates
                    .Where(a => a.IsBidirectional && a.AlternatePartId == line.ChildPartId)
                    .Select(a => (Part: a.PartId,
                        PerUnit: a.ConversionFactor is decimal factor && factor > 0m ? line.Quantity / factor : line.Quantity)));

            var seen = new HashSet<int>();
            foreach (var (part, perUnit) in substitutes)
            {
                if (part == line.ChildPartId || primaries.Contains((line.ParentPartId, part)) || !seen.Add(part))
                    continue;
                if (alternateAt.TryGetValue((line.ParentPartId, part), out var at))
                {
                    if (result[at].AlternateForPartId == line.ChildPartId)
                        result[at] = result[at] with { PerUnit = result[at].PerUnit + perUnit };
                    continue;
                }
                alternateAt[(line.ParentPartId, part)] = result.Count;
                result.Add(new IssuableComponent(line.ParentPartId, part, perUnit, line.ChildPartId));
            }
        }

        return result;
    }
}
