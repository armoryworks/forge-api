using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Pillar 3 — default <see cref="IPartSourcingResolver"/> implementation.
/// Reads vendor-specific sourcing terms (lead time / MOQ / pack size) from
/// the part's preferred VendorPart row. When no VendorPart is flagged
/// preferred, the part's <c>PreferredVendorId</c> names the vendor and that
/// vendor's VendorPart row (if any) supplies the terms, so installs whose
/// part and source flags disagree keep buying from the part's vendor until
/// someone edits them. When neither exists, every value comes back null and
/// consumers apply their own defaults (e.g. AutoPO uses 14 days when no lead
/// time resolves; reorder analysis treats null as "no cover-window data").
/// </summary>
public class PartSourcingResolver(AppDbContext db) : IPartSourcingResolver
{
    public async Task<PartSourcingValues> ResolveAsync(int partId, CancellationToken ct)
    {
        var resolved = await ResolveManyAsync([partId], ct);
        return resolved[partId];
    }

    public async Task<IReadOnlyDictionary<int, PartSourcingValues>> ResolveManyAsync(
        IReadOnlyCollection<int> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return new Dictionary<int, PartSourcingValues>();

        var idList = partIds.Distinct().ToList();

        var preferredRows = await db.VendorParts
            .AsNoTracking()
            .Where(vp => idList.Contains(vp.PartId) && vp.IsPreferred)
            .Select(vp => new SourceRow(vp.PartId, vp.VendorId, vp.LeadTimeDays, vp.MinOrderQty, vp.PackSize))
            .ToListAsync(ct);

        var sourceByPart = preferredRows
            .GroupBy(r => r.PartId)
            .ToDictionary(g => g.Key, g => g.First());

        var fallbackVendorByPart = new Dictionary<int, int>();
        var unresolvedIds = idList.Where(id => !sourceByPart.ContainsKey(id)).ToList();
        if (unresolvedIds.Count > 0)
        {
            fallbackVendorByPart = await db.Parts
                .AsNoTracking()
                .Where(p => unresolvedIds.Contains(p.Id) && p.PreferredVendorId != null)
                .ToDictionaryAsync(p => p.Id, p => p.PreferredVendorId!.Value, ct);

            if (fallbackVendorByPart.Count > 0)
            {
                var fallbackPartIds = fallbackVendorByPart.Keys.ToList();
                var fallbackRows = await db.VendorParts
                    .AsNoTracking()
                    .Where(vp => fallbackPartIds.Contains(vp.PartId))
                    .Select(vp => new SourceRow(vp.PartId, vp.VendorId, vp.LeadTimeDays, vp.MinOrderQty, vp.PackSize))
                    .ToListAsync(ct);

                foreach (var row in fallbackRows.Where(r => fallbackVendorByPart[r.PartId] == r.VendorId))
                    sourceByPart.TryAdd(row.PartId, row);
            }
        }

        var result = new Dictionary<int, PartSourcingValues>(idList.Count);

        foreach (var id in idList)
        {
            sourceByPart.TryGetValue(id, out var source);
            int? vendorId = source?.VendorId
                ?? (fallbackVendorByPart.TryGetValue(id, out var fallbackVendorId) ? fallbackVendorId : null);

            result[id] = new PartSourcingValues(
                PartId: id,
                PreferredVendorId: vendorId,
                LeadTimeDays: source?.LeadTimeDays,
                MinOrderQty: source?.MinOrderQty,
                PackSize: source?.PackSize,
                ResolvedFromVendorPart: source is not null);
        }

        return result;
    }

    private sealed record SourceRow(int PartId, int VendorId, int? LeadTimeDays, decimal? MinOrderQty, decimal? PackSize);
}
