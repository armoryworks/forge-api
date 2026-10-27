using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

/// <summary>Material specs in the shape the Material specs page lists them.</summary>
public static class MaterialSpecLookup
{
    /// <summary>Every material category and spec, categories in sort order with their specs under them.</summary>
    public static async Task<List<MaterialSpecResponseModel>> ListAsync(AppDbContext db, bool includeRetired, CancellationToken cancellationToken)
    {
        var rows = await db.ReferenceData.AsNoTracking()
            .Where(r => r.GroupCode == CreateMaterialSpecHandler.GroupCode && (includeRetired || r.IsActive))
            .Select(r => new { r.Id, r.Code, r.Label, r.ParentId, r.SortOrder, r.IsActive, r.IsSeedData })
            .ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var partCounts = await db.Parts.AsNoTracking()
            .Where(p => p.MaterialSpecId != null && ids.Contains(p.MaterialSpecId.Value))
            .GroupBy(p => p.MaterialSpecId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count, cancellationToken);
        var labels = rows.ToDictionary(r => r.Id, r => r.Label);
        var categorySort = rows.Where(r => r.ParentId == null).ToDictionary(r => r.Id, r => r.SortOrder);

        return rows
            .OrderBy(r => r.ParentId is int parent ? categorySort.GetValueOrDefault(parent) : r.SortOrder)
            .ThenBy(r => r.ParentId ?? r.Id)
            .ThenBy(r => r.ParentId is null ? 0 : 1)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Label)
            .Select(r => new MaterialSpecResponseModel(
                r.Id, r.Code, r.Label, r.ParentId,
                r.ParentId is int parent ? labels.GetValueOrDefault(parent) : null,
                r.SortOrder, r.IsActive, r.IsSeedData, partCounts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    public static async Task<MaterialSpecResponseModel> OneAsync(AppDbContext db, int id, CancellationToken ct)
        => (await ListAsync(db, includeRetired: true, ct)).Single(r => r.Id == id);
}
