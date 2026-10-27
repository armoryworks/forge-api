using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public static class PickLinePlanner
{
    public static async Task<List<PickLine>> PlanAsync(
        AppDbContext db, IReadOnlyList<ShipmentLine> shipmentLines, PickWaveStrategy strategy, CancellationToken ct)
    {
        var pickable = shipmentLines.Where(sl => sl.PartId.HasValue && sl.Quantity > 0).ToList();
        if (pickable.Count == 0) return [];

        var partIds = pickable.Select(sl => sl.PartId!.Value).Distinct().ToList();
        var bins = await db.BinContents.AsNoTracking()
            .Where(b => b.EntityType == "part" && partIds.Contains(b.EntityId)
                && b.RemovedAt == null && b.Quantity > 0 && b.Status != BinContentStatus.QcHold
                && b.Location.IsActive)
            .OrderBy(b => b.PlacedAt).ThenBy(b => b.Id)
            .ToListAsync(ct);

        var firstBinByPart = bins.GroupBy(b => b.EntityId).ToDictionary(g => g.Key, g => g.First());

        int? fallback = null;
        if (pickable.Any(sl => !firstBinByPart.ContainsKey(sl.PartId!.Value)))
        {
            fallback = await db.StorageLocations.AsNoTracking()
                .Where(l => l.IsActive)
                .OrderByDescending(l => l.IsDefault).ThenBy(l => l.SortOrder).ThenBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    "No storage location is set up — add one before building a pick wave.");
        }

        var locations = new Dictionary<int, (string Name, int? ParentId)>();
        var toLoad = firstBinByPart.Values.Select(b => b.LocationId).Append(fallback ?? 0).Where(id => id > 0).Distinct().ToList();
        while (toLoad.Count > 0)
        {
            var batch = await db.StorageLocations.AsNoTracking()
                .Where(l => toLoad.Contains(l.Id))
                .Select(l => new { l.Id, l.Name, l.ParentId })
                .ToListAsync(ct);
            foreach (var location in batch) locations[location.Id] = (location.Name, location.ParentId);
            toLoad = batch.Where(l => l.ParentId is int parent && !locations.ContainsKey(parent))
                .Select(l => l.ParentId!.Value).Distinct().ToList();
        }

        string PathOf(int locationId)
        {
            var names = new List<string>();
            var seen = new HashSet<int>();
            int? current = locationId;
            while (current is int id && seen.Add(id) && locations.TryGetValue(id, out var location))
            {
                names.Add(location.Name);
                current = location.ParentId;
            }
            names.Reverse();
            return string.Join(" / ", names);
        }

        var planned = pickable.Select(sl =>
        {
            var bin = firstBinByPart.GetValueOrDefault(sl.PartId!.Value);
            var locationId = bin?.LocationId ?? fallback!.Value;
            return new PickLine
            {
                ShipmentLineId = sl.Id,
                PartId = sl.PartId!.Value,
                FromLocationId = locationId,
                FromBinId = bin?.Id,
                BinPath = PathOf(locationId),
                RequestedQuantity = sl.Quantity,
                PickedQuantity = 0,
                Status = PickLineStatus.Pending,
            };
        }).ToList();

        var shipmentOf = pickable.ToDictionary(sl => sl.Id, sl => sl.ShipmentId);
        var ordered = strategy is PickWaveStrategy.Discrete or PickWaveStrategy.WaveByCarrier
            ? planned.OrderBy(l => shipmentOf[l.ShipmentLineId]).ThenBy(l => l.BinPath, StringComparer.OrdinalIgnoreCase)
            : planned.OrderBy(l => l.BinPath, StringComparer.OrdinalIgnoreCase).ThenBy(l => shipmentOf[l.ShipmentLineId]);

        var sortOrder = 0;
        var result = ordered.ThenBy(l => l.ShipmentLineId).ToList();
        foreach (var line in result) line.SortOrder = ++sortOrder;
        return result;
    }
}
