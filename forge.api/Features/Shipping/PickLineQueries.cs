using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public static class PickLineQueries
{
    public static async Task<List<PickLineResponseModel>> ListAsync(AppDbContext db, IQueryable<PickLine> lines, CancellationToken ct)
    {
        var rows = await lines
            .AsNoTracking()
            .OrderBy(l => l.Wave.ReleasedAt).ThenBy(l => l.WaveId).ThenBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.FromBinId,
                Line = new PickLineResponseModel
                {
                    Id = l.Id,
                    WaveId = l.WaveId,
                    WaveNumber = l.Wave.WaveNumber,
                    ShipmentLineId = l.ShipmentLineId,
                    ShipmentNumber = l.ShipmentLine.Shipment.ShipmentNumber,
                    PartId = l.PartId,
                    PartNumber = l.Part.PartNumber,
                    PartDescription = l.Part.Description ?? l.Part.Name,
                    FromLocationName = l.FromLocation.Name,
                    BinPath = l.BinPath,
                    RequestedQuantity = l.RequestedQuantity,
                    PickedQuantity = l.PickedQuantity,
                    Status = l.Status,
                    SortOrder = l.SortOrder,
                    PickedByUserId = l.PickedByUserId,
                    PickedAt = l.PickedAt,
                    ShortNotes = l.ShortNotes,
                },
            })
            .ToListAsync(ct);

        var binIds = rows.Where(r => r.FromBinId.HasValue).Select(r => r.FromBinId!.Value).Distinct().ToList();
        if (binIds.Count == 0) return rows.Select(r => r.Line).ToList();

        var lots = await db.BinContents.IgnoreQueryFilters().AsNoTracking()
            .Where(b => binIds.Contains(b.Id) && b.LotNumber != null)
            .ToDictionaryAsync(b => b.Id, b => b.LotNumber, ct);
        return rows
            .Select(r => r.FromBinId is int bin && lots.TryGetValue(bin, out var lot) ? r.Line with { LotNumber = lot } : r.Line)
            .ToList();
    }

    public static async Task<Dictionary<int, string>> UserNamesAsync(AppDbContext db, IEnumerable<int?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.LastName + ", " + u.FirstName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
}
