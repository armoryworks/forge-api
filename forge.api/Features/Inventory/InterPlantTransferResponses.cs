using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

/// <summary>
/// Reads inter-plant transfers with their plants, parts and the full path of each line's source and destination
/// bin, so the list and the create response show the same thing.
/// </summary>
public static class InterPlantTransferResponses
{
    public static async Task<List<InterPlantTransferResponseModel>> LoadAsync(
        AppDbContext db, IQueryable<InterPlantTransfer> query, CancellationToken ct)
    {
        var transfers = await query
            .AsNoTracking()
            .Include(t => t.FromPlant)
            .Include(t => t.ToPlant)
            .Include(t => t.Lines).ThenInclude(l => l.Part)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var locations = await db.StorageLocations.AsNoTracking()
            .Select(l => new { l.Id, l.Name, l.ParentId })
            .ToDictionaryAsync(l => l.Id, l => (l.Name, l.ParentId), ct);

        string? PathOf(int? locationId)
        {
            if (locationId is not int id || !locations.TryGetValue(id, out var location))
                return null;
            var names = new List<string> { location.Name };
            var parentId = location.ParentId;
            var guard = 0;
            while (parentId is int next && locations.TryGetValue(next, out var parent) && guard++ < 50)
            {
                names.Insert(0, parent.Name);
                parentId = parent.ParentId;
            }
            return string.Join(" / ", names);
        }

        return transfers.Select(t => new InterPlantTransferResponseModel(
            t.Id, t.TransferNumber,
            t.FromPlantId, t.FromPlant.Name,
            t.ToPlantId, t.ToPlant.Name,
            t.Status, t.ShippedAt, t.ReceivedAt,
            t.TrackingNumber, t.Notes,
            t.Lines.Count,
            t.Lines.OrderBy(l => l.Id).Select(l => new InterPlantTransferLineResponseModel(
                l.Id, l.PartId, l.Part.PartNumber, l.Part.Description ?? l.Part.Name,
                l.Quantity, l.ReceivedQuantity, l.LotNumber,
                l.FromLocationId, PathOf(l.FromLocationId),
                l.ToLocationId, PathOf(l.ToLocationId))).ToList(),
            t.CreatedAt, t.UpdatedAt)).ToList();
    }
}
