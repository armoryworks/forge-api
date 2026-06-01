using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="IPartLandedCostService"/>. Reads the most-recent
/// receipts for a part and rolls them up into a single landed-cost
/// surface for the cost tab. The math itself is unsurprising — the
/// interesting code lives at receive time (PR3 freight allocation in
/// <c>ReceiveItemsHandler</c>) and at part edit time (<c>VendorPart</c>
/// HtsCode + CountryOfOrigin lookup driving the duty component).
///
/// <para>FX adjustment is reported as 0 today. The PO carries a locked
/// FxRate (PR2.5) but the live conversion path goes through
/// <c>ICurrencyService</c> stubs; until those are real, we record the
/// rate but show 0 adjustment so the cost tab doesn't lie.</para>
/// </summary>
public class PartLandedCostService(
    AppDbContext db,
    ITariffResolver tariffResolver,
    ICurrencyService currencyService) : IPartLandedCostService
{
    public async Task<PartLandedCostResponseModel> GetForPartAsync(int partId, int maxReceipts, CancellationToken ct)
    {
        if (maxReceipts <= 0) maxReceipts = 3;

        var part = await db.Parts
            .AsNoTracking()
            .Where(p => p.Id == partId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Part {partId} not found");

        var baseCurrency = await currencyService.GetBaseCurrencyAsync(ct);

        // Pull the recent receipts that have AllocatedFreight populated
        // (PR3 freight-captured shipments). Records from before PR3 don't
        // contribute to the average; receipts where the buyer skipped
        // freight capture also fall through.
        var receipts = await db.ReceivingRecords
            .AsNoTracking()
            .Include(r => r.PurchaseOrderLine).ThenInclude(l => l.PurchaseOrder).ThenInclude(po => po.Vendor)
            .Include(r => r.PurchaseOrderLine).ThenInclude(l => l.PurchaseUnit)
            .Where(r => r.PurchaseOrderLine.PartId == partId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(maxReceipts * 2) // over-fetch, then filter
            .ToListAsync(ct);

        // Vendor-part lookup for HtsCode + CountryOfOrigin per (vendor, part).
        var vendorIds = receipts.Select(r => r.PurchaseOrderLine.PurchaseOrder.VendorId).Distinct().ToList();
        var vendorParts = await db.VendorParts
            .AsNoTracking()
            .Where(vp => vp.PartId == partId && vendorIds.Contains(vp.VendorId))
            .Select(vp => new { vp.VendorId, vp.HtsCode, vp.CountryOfOrigin })
            .ToListAsync(ct);
        var vpMap = vendorParts.ToDictionary(vp => vp.VendorId);

        var receiptModels = new List<PartLandedCostReceiptModel>();
        foreach (var rec in receipts)
        {
            // Skip records without freight allocation — they can't yield
            // a precise landed cost. Pre-PR3 records all fall here.
            if (!rec.AllocatedFreight.HasValue) continue;

            var line = rec.PurchaseOrderLine;
            var po = line.PurchaseOrder;

            // UoM purchase-units effort — when the line is priced per purchase unit
            // (e.g. $50 per 4×8 sheet), UnitPrice and QuantityReceived are in option-units.
            // Divide by the option's content to report cost per *base/stock* unit ($50/32 sqft
            // = $1.5625/sqft). Null option (or content ≤ 0) → already per base unit (divisor 1).
            var contentPerOption = line.PurchaseUnit?.ContentQuantity;
            var baseUnitsPerOption = contentPerOption is > 0 ? contentPerOption.Value : 1m;

            var basePrice = line.UnitPrice / baseUnitsPerOption;
            var freightPerOption = rec.QuantityReceived > 0 ? rec.AllocatedFreight.Value / rec.QuantityReceived : 0m;
            var freightPerUnit = freightPerOption / baseUnitsPerOption;

            var receiptDate = DateOnly.FromDateTime(rec.CreatedAt.UtcDateTime);
            vpMap.TryGetValue(po.VendorId, out var vp);
            var dutyRate = await tariffResolver.ResolveAsync(
                vp?.HtsCode, vp?.CountryOfOrigin, receiptDate, ct);
            var dutyPerUnit = dutyRate > 0 ? Math.Round(basePrice * (dutyRate / 100m), 4) : 0m;

            // FX adjustment placeholder. PR4 will wire the real conversion.
            var fxAdj = 0m;

            var landed = basePrice + freightPerUnit + dutyPerUnit + fxAdj;

            receiptModels.Add(new PartLandedCostReceiptModel(
                ReceivingRecordId: rec.Id,
                ReceiptNumber: rec.ReceiptNumber,
                VendorId: po.VendorId,
                VendorName: po.Vendor.CompanyName,
                PurchaseOrderId: po.Id,
                PurchaseOrderNumber: po.PONumber,
                ReceivedAt: rec.CreatedAt,
                QuantityReceived: rec.QuantityReceived,
                BaseUnitPrice: basePrice,
                AllocatedFreightPerUnit: freightPerUnit,
                DutyPerUnit: dutyPerUnit,
                FxAdjustmentPerUnit: fxAdj,
                LandedUnitCost: Math.Round(landed, 4)));

            if (receiptModels.Count >= maxReceipts) break;
        }

        // Average over what we collected. When nothing qualifies, leave nulls.
        decimal? avgLanded = null;
        decimal avgBase = 0m, avgFreight = 0m, avgDuty = 0m, avgFx = 0m;
        if (receiptModels.Count > 0)
        {
            avgLanded = Math.Round(receiptModels.Average(r => r.LandedUnitCost), 4);
            avgBase = Math.Round(receiptModels.Average(r => r.BaseUnitPrice), 4);
            avgFreight = Math.Round(receiptModels.Average(r => r.AllocatedFreightPerUnit), 4);
            avgDuty = Math.Round(receiptModels.Average(r => r.DutyPerUnit), 4);
            avgFx = Math.Round(receiptModels.Average(r => r.FxAdjustmentPerUnit), 4);
        }

        // Vendor comparison: each vendor's most recent landed unit cost
        // (uses the same receipt-level computation). Group the receipt
        // models by vendor and pick the freshest.
        var vendorCompare = receiptModels
            .GroupBy(r => r.VendorId)
            .Select(g =>
            {
                var freshest = g.OrderByDescending(r => r.ReceivedAt).First();
                return new VendorLandedCostComparisonModel(
                    g.Key,
                    freshest.VendorName,
                    freshest.LandedUnitCost,
                    freshest.ReceivedAt);
            })
            .OrderBy(v => v.MostRecentLandedUnitCost)
            .ToList();

        return new PartLandedCostResponseModel(
            PartId: part.Id,
            PartNumber: part.PartNumber,
            BaseCurrency: baseCurrency,
            AverageLandedUnitCost: avgLanded,
            ReceiptCountUsed: receiptModels.Count,
            AverageBaseUnitPrice: avgBase,
            AverageFreightPerUnit: avgFreight,
            AverageDutyPerUnit: avgDuty,
            AverageFxAdjustmentPerUnit: avgFx,
            RecentReceipts: receiptModels,
            VendorComparison: vendorCompare);
    }
}
