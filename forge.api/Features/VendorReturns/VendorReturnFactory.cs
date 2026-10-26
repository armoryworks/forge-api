using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.VendorCredits;
using Forge.Api.Services.Fx;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.VendorReturns;

/// <summary>
/// Opens the return to vendor for an NCR dispositioned Return To Vendor: one line per held bin row of the NCR's lot,
/// up to the defective (else affected) quantity. The vendor, purchase order line and receipt come from the lot's
/// receipt, else from the NCR; the return takes that purchase order's currency and the receipt's rate, and values each
/// unit at the purchase price. Without held stock or a vendor nothing is opened and the NCR's activity says why.
/// </summary>
public static class VendorReturnFactory
{
    public static async Task<VendorReturn?> OpenForNcrAsync(AppDbContext db, NonConformance ncr, int userId, CancellationToken ct)
    {
        if (await db.VendorReturns.AnyAsync(r => r.NonConformanceId == ncr.Id && r.Status != VendorReturnStatus.Closed, ct))
            return null;

        var held = string.IsNullOrWhiteSpace(ncr.LotNumber)
            ? []
            : await db.BinContents
                .Where(bc => bc.EntityType == "part" && bc.EntityId == ncr.PartId && bc.LotNumber == ncr.LotNumber
                    && bc.RemovedAt == null && bc.Status == BinContentStatus.QcHold)
                .OrderBy(bc => bc.PlacedAt).ThenBy(bc => bc.Id)
                .ToListAsync(ct);
        var available = held.Sum(r => r.Quantity - r.ReservedQuantity);
        if (available <= 0m)
        {
            db.LogActivityAt("vendor-return-skipped",
                $"{ncr.NcrNumber} has no stock on quality hold, so no return to vendor was opened",
                ("NonConformance", ncr.Id));
            return null;
        }

        var lot = await db.LotRecords.AsNoTracking()
            .Where(l => l.PartId == ncr.PartId && l.LotNumber == ncr.LotNumber)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(ct);
        var poLineId = lot?.PurchaseOrderLineId ?? ncr.PurchaseOrderLineId;
        var poLine = poLineId is int lineId
            ? await db.PurchaseOrderLines.AsNoTracking().Include(l => l.PurchaseOrder)
                .FirstOrDefaultAsync(l => l.Id == lineId, ct)
            : null;
        var receipt = poLine is null
            ? null
            : await db.Set<ReceivingRecord>().AsNoTracking()
                .Where(r => r.PurchaseOrderLineId == poLine.Id && (ncr.LotNumber == null || r.LotNumber == ncr.LotNumber))
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);

        var vendorId = poLine?.PurchaseOrder.VendorId ?? ncr.VendorId;
        if (vendorId is not int vendor)
        {
            db.LogActivityAt("vendor-return-skipped",
                $"{ncr.NcrNumber} names no vendor and its lot has no purchase receipt, so no return to vendor was opened",
                ("NonConformance", ncr.Id));
            return null;
        }

        var currencies = new DocumentCurrencyResolver(db);
        var vendorCurrency = await db.Vendors.AsNoTracking().Where(v => v.Id == vendor).Select(v => v.CurrencyId).FirstOrDefaultAsync(ct);
        var currencyId = await currencies.EffectiveCurrencyIdAsync(poLine?.PurchaseOrder.CurrencyId ?? vendorCurrency, ct);
        var fxRate = receipt?.FxRate ?? poLine?.PurchaseOrder.FxRate ?? 1m;
        var unitPrice = poLine?.UnitPrice
            ?? await db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.PartId == ncr.PartId && l.PurchaseOrder.VendorId == vendor)
                .OrderByDescending(l => l.Id)
                .Select(l => l.UnitPrice)
                .FirstOrDefaultAsync(ct);

        var target = ncr.DefectiveQuantity is > 0 ? ncr.DefectiveQuantity.Value
            : ncr.AffectedQuantity > 0 ? ncr.AffectedQuantity
            : available;
        var remaining = Math.Min(target, available);

        var rtv = new VendorReturn
        {
            VendorId = vendor,
            NonConformanceId = ncr.Id,
            PurchaseOrderId = poLine?.PurchaseOrderId,
            PurchaseOrderLineId = poLine?.Id,
            ReceivingRecordId = receipt?.Id,
            CurrencyId = currencyId,
            FxRate = fxRate,
            Status = VendorReturnStatus.Open,
            CreatedByUserId = userId,
        };
        var number = 1;
        foreach (var row in held)
        {
            if (remaining <= 0m)
                break;
            var take = Math.Min(remaining, row.Quantity - row.ReservedQuantity);
            if (take <= 0m)
                continue;
            rtv.Lines.Add(new VendorReturnLine
            {
                LineNumber = number++,
                PartId = ncr.PartId,
                LotNumber = ncr.LotNumber,
                LocationId = row.LocationId,
                BinContentId = row.Id,
                Quantity = take,
                UnitPrice = unitPrice,
                UnitCost = Math.Round(unitPrice * fxRate, 6, MidpointRounding.AwayFromZero),
            });
            remaining -= take;
        }

        rtv.ReturnNumber = await VendorCreditMemoRules.NextReturnNumberAsync(db, ct);
        db.VendorReturns.Add(rtv);
        await db.SaveChangesAsync(ct);
        db.LogActivityAt("vendor-return-opened",
            $"Return to vendor {rtv.ReturnNumber} opened for {rtv.Lines.Sum(l => l.Quantity):0.####} of lot {ncr.LotNumber}",
            ("NonConformance", ncr.Id), ("VendorReturn", rtv.Id), ("Vendor", vendor));
        return rtv;
    }
}
