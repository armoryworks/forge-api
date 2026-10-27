using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

/// <summary>
/// Lot-aware reads and writes of a part's bin contents for the scanner handlers. A bin can hold one
/// active row per lot plus an un-lotted row, so the scanner treats the location as one total: draws come
/// from un-lotted content first, then the oldest lots, and stock put back lands on the row for its lot.
/// A reversal's removal takes back only the free units of the lot it undoes (any lot for a scan that recorded no
/// movements) and refuses, before changing anything, when they fall short. Issues and moves draw only stock that
/// is not on quality hold, and issues to a job also skip expired lots.
/// </summary>
public static class ScanBinStock
{
    public static async Task<List<BinContent>> ActiveRowsAsync(AppDbContext db, int partId, int locationId, CancellationToken ct)
    {
        await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == partId
                && bc.LocationId == locationId
                && bc.RemovedAt == null
                && bc.OwnerVendorId == null)
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

    public static async Task<List<BinContent>> IssuableRowsAsync(
        AppDbContext db, int partId, int locationId, decimal quantity, DateOnly shopToday, CancellationToken ct)
    {
        var drawable = await DrawableRowsAsync(db, partId, locationId, quantity, ct);
        var expiredLots = await LotExpiry.ExpiredLotsAsync(db, partId, shopToday, ct);
        if (expiredLots.Count == 0)
            return drawable;

        bool IsExpired(BinContent bc) => bc.LotNumber is not null && expiredLots.ContainsKey(bc.LotNumber);
        var issuable = drawable.Where(bc => !IsExpired(bc)).ToList();
        var expired = drawable.Where(bc => IsExpired(bc) && bc.Quantity - bc.ReservedQuantity > 0).ToList();
        var free = issuable.Sum(bc => bc.Quantity - bc.ReservedQuantity);
        if (expired.Count > 0 && free < quantity
            && free + expired.Sum(bc => bc.Quantity - bc.ReservedQuantity) >= quantity)
            throw LotExpiry.Refusal(expired[0].LotNumber!, expiredLots[expired[0].LotNumber!]);
        return issuable;
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
        AppDbContext db, int partId, int locationId, string? lotNumber, bool anyLot, decimal quantity,
        int userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = (await ActiveRowsAsync(db, partId, locationId, ct))
            .Where(bc => anyLot || bc.LotNumber == lotNumber)
            .ToList();
        var free = rows.Sum(bc => bc.Quantity - bc.ReservedQuantity);
        if (free < quantity)
            throw new InvalidOperationException(
                $"Cannot reverse this scan: it put {quantity} at the location" +
                (anyLot || lotNumber is null ? "" : $" in lot {lotNumber}") +
                $", but only {free} of it is still there and not reserved.");
        BinContentDrawDown.Take(rows, quantity, userId, now);
    }

    private static bool IsActiveRow(BinContent bc, int partId, int locationId)
        => bc.EntityType == "part"
            && bc.EntityId == partId
            && bc.LocationId == locationId
            && bc.RemovedAt == null
            && bc.OwnerVendorId == null;
}
