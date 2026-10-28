using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Tolerances;

/// <summary>
/// The receipt tolerance a subcontract receive-back is measured with, against the quantity sent: the
/// subcontract purchase-order line's own tolerance when the order has one, else the vendor's, else the
/// company purchasing default.
/// </summary>
public static class SubcontractTolerances
{
    public const string Kind = "subcontract-receive-back";

    public static async Task<Dictionary<int, QuantityTolerance>> ForOrdersAsync(
        AppDbContext db, IReadOnlyCollection<(int OrderId, int VendorId, int? PurchaseOrderId)> orders, CancellationToken ct)
    {
        if (orders.Count == 0)
            return [];

        var vendorIds = orders.Select(o => o.VendorId).Distinct().ToList();
        var vendors = await db.Vendors.AsNoTracking()
            .Where(v => vendorIds.Contains(v.Id))
            .Select(v => new { v.Id, v.OverReceiptTolerancePct, v.UnderReceiptTolerancePct })
            .ToDictionaryAsync(v => v.Id, ct);

        var poIds = orders.Where(o => o.PurchaseOrderId.HasValue).Select(o => o.PurchaseOrderId!.Value).Distinct().ToList();
        var poLines = (await db.PurchaseOrderLines.AsNoTracking()
                .Where(l => poIds.Contains(l.PurchaseOrderId))
                .Select(l => new { l.Id, l.PurchaseOrderId, l.OverReceiptTolerancePct, l.UnderReceiptTolerancePct })
                .ToListAsync(ct))
            .GroupBy(l => l.PurchaseOrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Id).First());

        var defaults = await QuantityTolerances.PurchaseDefaultsAsync(db, ct);
        return orders.ToDictionary(o => o.OrderId, o =>
        {
            var vendor = vendors.GetValueOrDefault(o.VendorId);
            var line = o.PurchaseOrderId is int poId ? poLines.GetValueOrDefault(poId) : null;
            return QuantityTolerances.Resolve(
                line?.OverReceiptTolerancePct, line?.UnderReceiptTolerancePct,
                vendor?.OverReceiptTolerancePct, vendor?.UnderReceiptTolerancePct, defaults);
        });
    }
}
