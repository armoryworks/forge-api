using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Services.Fulfilment;

/// <summary>
/// Back-to-back purchasing: a real PO, linked to the sales order line, shipping to our own warehouse. Receiving
/// it reserves the goods to that line (the receive command does this as it stocks the bin), so they are held for
/// the order and drawn first when it ships. <see cref="LinkReceiptToSalesOrderAsync"/> reserves an earlier receipt
/// after the fact, for example once stock comes off a receiving-inspection hold.
/// </summary>
public sealed class BuiltInBackToBackService(AppDbContext db, LinkedPurchaseOrders linkedPurchaseOrders) : IBackToBackService
{
    public Task<PurchaseOrder> CreateBackToBackOrderAsync(
        int salesOrderLineId, CreateBackToBackRequestModel request, CancellationToken ct)
        => linkedPurchaseOrders.CreateAsync(
            salesOrderLineId, FulfilmentType.BackToBack, request.VendorId, request.Quantity, request.UnitCost,
            request.ExpectedDeliveryDate, request.Notes, ct);

    public async Task LinkReceiptToSalesOrderAsync(int purchaseOrderLineId, int receivingRecordId, CancellationToken ct)
    {
        var record = await db.ReceivingRecords.AsNoTracking()
            .Include(r => r.PurchaseOrderLine).ThenInclude(l => l.PurchaseOrder)
            .Include(r => r.PurchaseOrderLine).ThenInclude(l => l.PurchaseUnit)
            .FirstOrDefaultAsync(r => r.Id == receivingRecordId && r.PurchaseOrderLineId == purchaseOrderLineId, ct)
            ?? throw new KeyNotFoundException($"Receipt {receivingRecordId} not found on purchase order line {purchaseOrderLineId}");
        var line = record.PurchaseOrderLine;
        if (line.PurchaseOrder.FulfilmentType != FulfilmentType.BackToBack || line.SalesOrderLineId is not int soLineId)
            throw new InvalidOperationException($"Purchase order {line.PurchaseOrder.PONumber} is not a back-to-back order for a sales order line.");
        if (line.PartId is not int partId || record.StorageLocationId is not int locationId)
            throw new InvalidOperationException("This receipt put nothing into a bin, so there is nothing to reserve.");

        var baseQty = line.PurchaseUnit?.ContentQuantity is decimal content and > 0m
            ? record.QuantityReceived * content
            : record.QuantityReceived;
        var bins = await db.BinContents
            .Where(b => b.EntityType == "part" && b.EntityId == partId && b.LocationId == locationId
                && b.LotNumber == record.LotNumber && b.RemovedAt == null && b.Status != BinContentStatus.QcHold)
            .OrderBy(b => b.Id)
            .ToListAsync(ct);
        if (bins.Count == 0)
            throw new InvalidOperationException("The received stock is no longer in its bin, or it is still on hold.");

        var remaining = baseQty;
        foreach (var bin in bins)
        {
            if (remaining <= 0m)
                break;
            remaining -= await BackToBackReservations.ReserveAsync(db, soLineId, partId, bin, remaining, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BackToBackStatusResponseModel>> GetPendingBackToBacksAsync(CancellationToken ct)
    {
        var rows = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.SalesOrderLineId != null
                && l.PurchaseOrder.FulfilmentType == FulfilmentType.BackToBack
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && l.SalesOrderLine!.ShippedQuantity + l.SalesOrderLine.ShortClosedQuantity < l.SalesOrderLine.Quantity)
            .OrderBy(l => l.PurchaseOrder.ExpectedDeliveryDate).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.Id,
                l.PurchaseOrderId,
                l.PurchaseOrder.PONumber,
                PoStatus = l.PurchaseOrder.Status,
                l.PurchaseOrder.ExpectedDeliveryDate,
                Vendor = l.PurchaseOrder.Vendor.CompanyName,
                Remaining = l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity,
                l.OrderedQuantity,
                l.ReceivedQuantity,
                SoLineId = l.SalesOrderLine!.Id,
                l.SalesOrderLine.SalesOrderId,
                l.SalesOrderLine.SalesOrder.OrderNumber,
                Customer = l.SalesOrderLine.SalesOrder.Customer.Name,
                PartNumber = l.Part != null ? l.Part.PartNumber : string.Empty,
            })
            .ToListAsync(ct);

        var soLineIds = rows.Select(r => r.SoLineId).Distinct().ToList();
        var reserved = await db.Reservations.AsNoTracking()
            .Where(r => r.SalesOrderLineId != null && soLineIds.Contains(r.SalesOrderLineId.Value))
            .GroupBy(r => r.SalesOrderLineId!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(g => g.Key, g => g.Quantity, ct);

        return rows.Select(r => new BackToBackStatusResponseModel
        {
            SalesOrderId = r.SalesOrderId,
            SalesOrderNumber = r.OrderNumber,
            SalesOrderLineId = r.SoLineId,
            CustomerName = r.Customer,
            PartNumber = r.PartNumber,
            Quantity = r.OrderedQuantity,
            VendorName = r.Vendor,
            PurchaseOrderId = r.PurchaseOrderId,
            PurchaseOrderNumber = r.PONumber,
            PurchaseOrderLineId = r.Id,
            ExpectedDeliveryDate = r.ExpectedDeliveryDate,
            ReceivedQuantity = r.ReceivedQuantity,
            ReservedQuantity = reserved.GetValueOrDefault(r.SoLineId),
            Status = r.Remaining <= 0m ? "ReadyToShip" : r.ReceivedQuantity > 0m ? "PartiallyReceived" : r.PoStatus.ToString(),
        }).ToList();
    }
}
