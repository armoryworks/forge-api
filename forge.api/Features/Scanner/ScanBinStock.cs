using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

/// <summary>
/// Lot-aware reads and writes of a part's bin contents for the scanner handlers. A bin can hold one
/// active row per lot plus an un-lotted row, so the scanner treats the location as one total: draws come
/// from un-lotted content first, then the oldest lots, and stock put back lands on the row for its lot.
/// A reversal's removal starts with the row for the lot it undoes and does not protect reserved units,
/// because it takes back stock that an earlier scan put there. Issues and moves draw only stock that is not on
/// quality hold.
/// </summary>
public static class ScanBinStock
{
    public static async Task<List<BinContent>> ActiveRowsAsync(AppDbContext db, int partId, int locationId, CancellationToken ct)
    {
        await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LocationId == locationId
                && bc.RemovedAt == null)
            .LoadAsync(ct);

        return db.BinContents.Local
            .Where(bc => IsActiveRow(bc, partId, locationId))
            .OrderBy(bc => bc.LotNumber != null)
            .ThenBy(bc => bc.PlacedAt)
            .ThenBy(bc => bc.Id)
            .ToList();
    }

    public static async Task<List<BinContent>> DrawableRowsAsync(
        AppDbContext db, int partId, int locationId, decimal quantity, CancellationToken ct)
    {
        var rows = await ActiveRowsAsync(db, partId, locationId, ct);
        var drawable = rows.Where(bc => bc.Status != BinContentStatus.QcHold).ToList();
        var held = rows.Where(bc => bc.Status == BinContentStatus.QcHold && bc.Quantity > 0).ToList();
        var free = drawable.Sum(bc => bc.Quantity - bc.ReservedQuantity);
        if (held.Count > 0 && free < quantity && free + held.Sum(bc => bc.Quantity) >= quantity)
            throw await LotQualityHold.RefusalAsync(db, partId, held[0].LotNumber, ct);
        return drawable;
    }

    public static async Task<BinContent> AddAsync(
        AppDbContext db, int partId, int locationId, string? lotNumber, decimal quantity,
        int userId, DateTimeOffset now, CancellationToken ct)
    {
        var row = (await ActiveRowsAsync(db, partId, locationId, ct))
            .FirstOrDefault(bc => bc.LotNumber == lotNumber);

        if (row is not null)
        {
            row.Quantity += quantity;
            return row;
        }

        row = new BinContent
        {
            LocationId = locationId,
            EntityType = "part",
            EntityId = partId,
            Quantity = quantity,
            LotNumber = lotNumber,
            PlacedBy = userId,
            PlacedAt = now,
        };
        db.BinContents.Add(row);
        return row;
    }

    public static async Task RemoveAsync(
        AppDbContext db, int partId, int locationId, string? lotNumber, decimal quantity,
        int userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await ActiveRowsAsync(db, partId, locationId, ct);
        var remaining = quantity;
        foreach (var row in rows.OrderBy(r => r.LotNumber != lotNumber))
        {
            if (remaining <= 0)
                break;
            var take = Math.Min(row.Quantity, remaining);
            row.Quantity -= take;
            remaining -= take;
            if (row.Quantity <= 0)
            {
                row.Quantity = 0;
                row.RemovedAt = now;
                row.RemovedBy = userId;
            }
        }
    }

    private static bool IsActiveRow(BinContent bc, int partId, int locationId)
        => bc.EntityType == "part"
            && bc.EntityId == partId
            && bc.LocationId == locationId
            && bc.RemovedAt == null;
}
