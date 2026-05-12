using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Pillar 3 — default <see cref="IPartSourcingResolver"/> implementation.
/// Reads vendor-specific sourcing terms (lead time / MOQ / pack size) from
/// the part's preferred VendorPart row. The Part-level snapshot columns
/// were dropped along with the OEM-on-VendorPart move — there is no
/// fallback to <c>Part</c> here. When no preferred VendorPart exists for
/// a part, all three values come back as null and consumers must apply
/// their own defaults (e.g. AutoPO uses 14 days when no lead time
/// resolves; reorder analysis treats null as "no cover-window data").
/// </summary>
public class PartSourcingResolver(AppDbContext db) : IPartSourcingResolver
{
    public async Task<PartSourcingValues> ResolveAsync(int partId, CancellationToken ct)
    {
        var preferred = await db.VendorParts
            .AsNoTracking()
            .Where(vp => vp.PartId == partId && vp.IsPreferred)
            .Select(vp => new
            {
                vp.VendorId,
                vp.LeadTimeDays,
                vp.MinOrderQty,
                vp.PackSize,
            })
            .FirstOrDefaultAsync(ct);

        return new PartSourcingValues(
            PartId: partId,
            PreferredVendorId: preferred?.VendorId,
            LeadTimeDays: preferred?.LeadTimeDays,
            MinOrderQty: preferred?.MinOrderQty,
            PackSize: preferred?.PackSize,
            ResolvedFromVendorPart: preferred is not null);
    }

    public async Task<IReadOnlyDictionary<int, PartSourcingValues>> ResolveManyAsync(
        IReadOnlyCollection<int> partIds, CancellationToken ct)
    {
        if (partIds.Count == 0)
            return new Dictionary<int, PartSourcingValues>();

        // De-dup defensively — callers may not always pass a HashSet.
        var idList = partIds.Distinct().ToList();

        // Single query for all preferred VendorPart rows in the batch.
        var preferredRows = await db.VendorParts
            .AsNoTracking()
            .Where(vp => idList.Contains(vp.PartId) && vp.IsPreferred)
            .Select(vp => new
            {
                vp.PartId,
                vp.VendorId,
                vp.LeadTimeDays,
                vp.MinOrderQty,
                vp.PackSize,
            })
            .ToListAsync(ct);

        var preferredByPart = preferredRows.ToDictionary(r => r.PartId);

        var result = new Dictionary<int, PartSourcingValues>(idList.Count);

        foreach (var id in idList)
        {
            preferredByPart.TryGetValue(id, out var preferred);

            result[id] = new PartSourcingValues(
                PartId: id,
                PreferredVendorId: preferred?.VendorId,
                LeadTimeDays: preferred?.LeadTimeDays,
                MinOrderQty: preferred?.MinOrderQty,
                PackSize: preferred?.PackSize,
                ResolvedFromVendorPart: preferred is not null);
        }

        return result;
    }
}
