using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Services.Fulfilment;

/// <summary>
/// Raises a purchase order bought for one sales order line (drop-ship or back-to-back) through the normal PO
/// create command, so numbering, vendor checks, currency, payment terms and the approval workflow all apply,
/// then marks it with its fulfilment type and links its line to the sales order line. A line can be bought for
/// no more than it still needs: ordered, less what has shipped or is on a live standard shipment, less what live
/// linked POs already cover.
/// </summary>
public sealed class LinkedPurchaseOrders(AppDbContext db, ISender sender, IClock clock)
{
    private static readonly SalesOrderStatus[] BuyableStatuses =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.InProduction, SalesOrderStatus.PartiallyShipped];

    public async Task<PurchaseOrder> CreateAsync(
        int salesOrderLineId, FulfilmentType type, int vendorId, decimal? quantity, decimal? unitCost,
        DateTimeOffset? expectedDeliveryDate, string? notes, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await db.AcquireAdvisoryLockAsync(
            "sales-order-line.linked-po", salesOrderLineId.ToString(CultureInfo.InvariantCulture), ct);

        var line = await db.SalesOrderLines.AsNoTracking()
            .Include(l => l.Part)
            .Include(l => l.SalesOrder).ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(l => l.Id == salesOrderLineId, ct)
            ?? throw new KeyNotFoundException($"Sales order line {salesOrderLineId} not found");
        var order = line.SalesOrder;
        var label = type == FulfilmentType.DropShip ? "drop-ship" : "back-to-back";

        if (!BuyableStatuses.Contains(order.Status))
            throw new InvalidOperationException(
                $"Sales order {order.OrderNumber} is {order.Status}. Only a confirmed order that is still open can have a {label} purchase order.");
        if (line.IsFreight || line.Part?.InventoryClass == InventoryClass.Service)
            throw new InvalidOperationException($"Line {line.LineNumber} does not ship goods, so it cannot be bought {label}.");

        var open = await OpenToBuyAsync(line, ct);
        var qty = quantity ?? open;
        if (qty <= 0m)
            throw new InvalidOperationException($"Line {line.LineNumber} has nothing left to buy: it is already shipped or covered by purchase orders.");
        if (qty > open)
            throw new InvalidOperationException(
                $"Line {line.LineNumber} needs only {open:0.####} more; {qty:0.####} would buy more than the customer ordered.");
        if (unitCost is < 0m)
            throw new InvalidOperationException("Unit cost cannot be negative.");

        var vendor = vendorId > 0 ? vendorId : await PreferredVendorAsync(line, ct)
            ?? throw new InvalidOperationException($"Choose a vendor: {line.Part?.PartNumber ?? line.Description} has no preferred vendor.");
        var cost = unitCost ?? await VendorPriceAsync(vendor, line.PartId, qty, ct) ?? 0m;

        DropShipAddress? shipTo = null;
        if (type == FulfilmentType.DropShip)
            shipTo = await DropShipAddressAsync(order, ct)
                ?? throw new InvalidOperationException(
                    $"Sales order {order.OrderNumber} has no ship-to address and {order.Customer.Name} has no shipping address. Add one before drop-shipping.");

        var poNote = type == FulfilmentType.DropShip
            ? $"Drop-ship for sales order {order.OrderNumber} line {line.LineNumber}. Ship directly to {order.Customer.Name}: {shipTo!.Text}."
            : $"Back-to-back for sales order {order.OrderNumber} line {line.LineNumber} ({order.Customer.Name}).";
        if (!string.IsNullOrWhiteSpace(notes))
            poNote += " " + notes.Trim();

        var created = await sender.Send(new CreatePurchaseOrderCommand(
            vendor,
            JobId: null,
            Notes: poNote.Length > 2000 ? poNote[..2000] : poNote,
            Lines:
            [
                new CreatePurchaseOrderLineModel(
                    line.PartId,
                    PurchaseOrderLine.ClipDescription(line.Description),
                    qty,
                    cost,
                    $"For {order.OrderNumber} line {line.LineNumber}"),
            ],
            ExpectedDeliveryDate: expectedDeliveryDate), ct);

        var po = await db.PurchaseOrders.Include(p => p.Lines).FirstAsync(p => p.Id == created.Id, ct);
        po.FulfilmentType = type;
        po.DropShipAddressId = shipTo?.Id;
        po.OriginReference = $"{order.OrderNumber} line {line.LineNumber}";
        foreach (var poLine in po.Lines)
            poLine.SalesOrderLineId = line.Id;

        db.LogActivityAt(
            $"{label}-linked",
            $"Buys {qty:0.####} {label} for sales order {order.OrderNumber} line {line.LineNumber}",
            ("PurchaseOrder", po.Id));
        db.LogActivityAt(
            $"{label}-po-created",
            $"Line {line.LineNumber}: {label} purchase order {po.PONumber} for {qty:0.####}",
            ("SalesOrder", order.Id));
        await db.SaveChangesAsync(ct);

        if (tx is not null)
            await tx.CommitAsync(ct);
        return po;
    }

    /// <summary>The quantity of the line not yet shipped, on a live standard shipment, or covered by live linked POs.</summary>
    public async Task<decimal> OpenToBuyAsync(SalesOrderLine line, CancellationToken ct)
    {
        var onStandardShipments = await db.ShipmentLines.AsNoTracking()
            .Where(s => s.SalesOrderLineId == line.Id
                && s.Shipment.Status != ShipmentStatus.Cancelled
                && s.Shipment.FulfilmentType == FulfilmentType.Standard)
            .SumAsync(s => (decimal?)s.Quantity, ct) ?? 0m;
        var covered = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.SalesOrderLineId == line.Id && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled)
            .SumAsync(l => (decimal?)(l.OrderedQuantity - l.CancelledShortCloseQuantity), ct) ?? 0m;
        return Math.Max(0m, line.Quantity - onStandardShipments - covered);
    }

    private async Task<int?> PreferredVendorAsync(SalesOrderLine line, CancellationToken ct)
    {
        if (line.PartId is not int partId)
            return null;
        var preferred = await db.VendorParts.AsNoTracking()
            .Where(v => v.PartId == partId && v.IsApproved)
            .OrderByDescending(v => v.IsPreferred).ThenBy(v => v.Id)
            .Select(v => (int?)v.VendorId)
            .FirstOrDefaultAsync(ct);
        return preferred ?? line.Part?.PreferredVendorId;
    }

    private async Task<decimal?> VendorPriceAsync(int vendorId, int? partId, decimal quantity, CancellationToken ct)
    {
        if (partId is null)
            return null;
        var now = clock.UtcNow;
        return await db.VendorPartPriceTiers.AsNoTracking()
            .Where(t => t.VendorPart.VendorId == vendorId && t.VendorPart.PartId == partId
                && t.PurchaseUnitId == null
                && t.MinQuantity <= quantity
                && t.EffectiveFrom <= now && (t.EffectiveTo == null || t.EffectiveTo > now))
            .OrderByDescending(t => t.MinQuantity).ThenByDescending(t => t.EffectiveFrom)
            .Select(t => (decimal?)t.UnitPrice)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<DropShipAddress?> DropShipAddressAsync(SalesOrder order, CancellationToken ct)
    {
        var address = order.ShippingAddressId is int addressId
            ? await db.CustomerAddresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == addressId, ct)
            : await db.CustomerAddresses.AsNoTracking()
                .Where(a => a.CustomerId == order.CustomerId && a.IsActive
                    && (a.AddressType == AddressType.Shipping || a.AddressType == AddressType.Both))
                .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Id)
                .FirstOrDefaultAsync(ct);
        return address is null
            ? null
            : new DropShipAddress(address.Id, PurchaseOrderParties.OneLine(
                address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country));
    }

    private sealed record DropShipAddress(int Id, string Text);
}
