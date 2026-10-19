using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quality;

/// <summary>
/// The lot quality hold an NCR places on stock. Raising an NCR on a part and lot moves that lot's Stored part
/// contents to QcHold; a Use As Is or Rework disposition, or closing the NCR, moves them back to Stored unless
/// another NCR still holds the lot or an active recall covers it. A Scrap disposition draws the scrapped
/// quantity out of the held contents (<see cref="ScrapHeldAsync"/>), so closing releases only what was not
/// scrapped; a Return To Vendor disposition keeps the hold through close, because the stock is going back to the
/// vendor. Reopening a closed NCR whose disposition kept the hold places it again on whatever of the lot is still
/// stored. Stock-out paths refuse held stock with <see cref="Message"/>. Every hold, release and scrap is logged
/// on the NCR and, when a lot record exists, the lot.
/// </summary>
public static class LotQualityHold
{
    public static string Message(string? lotNumber, string? ncrNumber) => (lotNumber, ncrNumber) switch
    {
        (null, _) => "This stock is on quality hold.",
        (_, null) => $"Lot {lotNumber} is on quality hold.",
        _ => $"Lot {lotNumber} is on quality hold (NCR {ncrNumber}).",
    };

    public static async Task<QualityHoldException> RefusalAsync(
        AppDbContext db, int partId, string? lotNumber, CancellationToken ct)
        => new(Message(lotNumber, await db.FindQualityHoldNcrNumberAsync(partId, lotNumber, ct)));

    public static async Task<bool> PlaceAsync(AppDbContext db, NonConformance ncr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ncr.LotNumber))
            return false;

        var rows = await LotRows(db, ncr, BinContentStatus.Stored).ToListAsync(ct);
        if (rows.Count == 0)
            return false;

        foreach (var row in rows)
            row.Status = BinContentStatus.QcHold;

        var quantity = rows.Sum(r => r.Quantity);
        await LogAsync(db, ncr, "quality-hold-placed",
            $"Lot {ncr.LotNumber} put on quality hold: {quantity:0.####} held by {ncr.NcrNumber}", ct);
        return true;
    }

    public static async Task<decimal> ReleaseAsync(AppDbContext db, NonConformance ncr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ncr.LotNumber))
            return 0;

        var rows = await LotRows(db, ncr, BinContentStatus.QcHold).ToListAsync(ct);
        if (rows.Count == 0)
            return 0;

        var otherHold = await db.NcrsHoldingLot(ncr.PartId, ncr.LotNumber).AnyAsync(n => n.Id != ncr.Id, ct);
        if (otherHold || await db.IsLotUnderActiveRecallAsync(ncr.LotNumber, ct))
            return 0;

        foreach (var row in rows)
            row.Status = BinContentStatus.Stored;

        var quantity = rows.Sum(r => r.Quantity);
        await LogAsync(db, ncr, "quality-hold-released",
            $"Lot {ncr.LotNumber} released from quality hold: {quantity:0.####} released by {ncr.NcrNumber}", ct);
        return quantity;
    }

    public static async Task<decimal> ScrapHeldAsync(
        AppDbContext db, NonConformance ncr, int userId, DateTimeOffset at, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ncr.LotNumber))
        {
            db.LogActivityAt("quality-scrap",
                $"{ncr.NcrNumber} has no lot, so no held stock was found to scrap; nothing was removed from inventory",
                ("NonConformance", ncr.Id));
            return 0;
        }

        var rows = await LotRows(db, ncr, BinContentStatus.QcHold)
            .OrderBy(r => r.PlacedAt).ThenBy(r => r.Id)
            .ToListAsync(ct);
        var available = rows.Sum(r => r.Quantity - r.ReservedQuantity);
        var target = ncr.DefectiveQuantity is > 0 ? ncr.DefectiveQuantity.Value
            : ncr.AffectedQuantity > 0 ? ncr.AffectedQuantity
            : available;

        var remaining = Math.Min(target, available);
        var scrapped = 0m;
        foreach (var row in rows)
        {
            if (remaining <= 0)
                break;
            var take = Math.Min(remaining, row.Quantity - row.ReservedQuantity);
            if (take <= 0)
                continue;

            row.Quantity -= take;
            if (row.Quantity == 0)
            {
                row.RemovedAt = at;
                row.RemovedBy = userId;
            }
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = ncr.PartId,
                Quantity = take,
                LotNumber = ncr.LotNumber,
                FromLocationId = row.LocationId,
                MovedBy = userId,
                MovedAt = at,
                Reason = BinMovementReason.Adjustment,
                Notes = $"Scrapped by {ncr.NcrNumber}",
            });
            remaining -= take;
            scrapped += take;
        }

        await LogAsync(db, ncr, "quality-scrap", scrapped > 0
            ? $"Lot {ncr.LotNumber} scrapped: {scrapped:0.####} removed from inventory by {ncr.NcrNumber}"
            : $"Lot {ncr.LotNumber} has no held stock to scrap; nothing was removed from inventory by {ncr.NcrNumber}", ct);
        return scrapped;
    }

    private static IQueryable<BinContent> LotRows(AppDbContext db, NonConformance ncr, BinContentStatus status)
        => db.BinContents.Where(bc => bc.EntityType == "part"
            && bc.EntityId == ncr.PartId
            && bc.LotNumber == ncr.LotNumber
            && bc.RemovedAt == null
            && bc.Status == status);

    private static async Task LogAsync(
        AppDbContext db, NonConformance ncr, string action, string description, CancellationToken ct)
    {
        var lotId = await db.LotRecords
            .Where(l => l.PartId == ncr.PartId && l.LotNumber == ncr.LotNumber)
            .OrderByDescending(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(ct);

        if (lotId is int id)
            db.LogActivityAt(action, description, ("NonConformance", ncr.Id), ("Lot", id));
        else
            db.LogActivityAt(action, description, ("NonConformance", ncr.Id));
    }
}
