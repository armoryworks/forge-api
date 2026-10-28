using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.PurchaseOrders;
using Forge.Api.Features.Shipments;
using Forge.Api.Features.Tolerances;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Services.Fulfilment;

/// <summary>
/// Drop-ship purchasing: a real PO, linked to the sales order line, telling the vendor to ship straight to the
/// customer. Confirming the vendor's delivery receives the PO line as a non-stock receipt (no bin, no movement;
/// with CAP-ACCT-FULLGL it posts Dr COGS / Cr GRNI at the PO cost) and records a delivered drop-ship shipment for
/// the sales order line in one transaction, so the line counts as shipped and can be invoiced.
/// </summary>
public sealed class BuiltInDropShipService(
    AppDbContext db,
    LinkedPurchaseOrders linkedPurchaseOrders,
    IShipmentRepository shipments,
    IBusinessIdentifierService identifiers,
    IMediator mediator,
    IClock clock) : IDropShipService
{
    public Task<PurchaseOrder> CreateDropShipPurchaseOrderAsync(
        int salesOrderLineId, CreateDropShipRequestModel request, CancellationToken ct)
        => linkedPurchaseOrders.CreateAsync(
            salesOrderLineId, FulfilmentType.DropShip, request.VendorId, request.Quantity, request.UnitCost,
            request.ExpectedDeliveryDate, request.Notes, ct);

    public async Task ConfirmDropShipDeliveryAsync(
        int purchaseOrderLineId, decimal deliveredQuantity, string? trackingNumber, CancellationToken ct)
    {
        if (deliveredQuantity <= 0m)
            throw new InvalidOperationException("Delivered quantity must be greater than zero.");

        var tracking = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim();
        if (tracking is { Length: > 100 })
            throw new InvalidOperationException("Tracking number must be 100 characters or fewer.");

        Shipment shipment;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.AcquireAdvisoryLockAsync(
                "purchase-order-line.drop-ship-confirm", purchaseOrderLineId.ToString(CultureInfo.InvariantCulture), ct);

            var poLine = await db.PurchaseOrderLines
                .Include(l => l.PurchaseOrder).ThenInclude(p => p.Vendor)
                .FirstOrDefaultAsync(l => l.Id == purchaseOrderLineId, ct)
                ?? throw new KeyNotFoundException($"Purchase order line {purchaseOrderLineId} not found");
            var po = poLine.PurchaseOrder;
            if (po.FulfilmentType != FulfilmentType.DropShip || poLine.SalesOrderLineId is not int soLineId)
                throw new InvalidOperationException($"Purchase order {po.PONumber} is not a drop-ship for a sales order line.");
            if (deliveredQuantity > poLine.RemainingQuantity)
                throw new InvalidOperationException(
                    $"Only {poLine.RemainingQuantity:0.####} of this line is still to be delivered.");

            var soLine = await db.SalesOrderLines.AsNoTracking()
                .Include(l => l.SalesOrder)
                .FirstOrDefaultAsync(l => l.Id == soLineId, ct)
                ?? throw new KeyNotFoundException($"Sales order line {soLineId} not found");
            if (soLine.SalesOrder.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Draft)
                throw new InvalidOperationException(
                    $"Sales order {soLine.SalesOrder.OrderNumber} is {soLine.SalesOrder.Status}; a delivery cannot be recorded against it.");

            await mediator.Send(new ReceiveItemsCommand(
                po.Id,
                [new ReceiveLineModel(poLine.Id, deliveredQuantity, null, tracking is null ? "Drop-ship delivery" : $"Drop-ship delivery, tracking {tracking}")],
                DropShipDelivery: true), ct);

            var now = clock.UtcNow;
            shipment = new Shipment
            {
                ShipmentNumber = await shipments.GenerateNextShipmentNumberAsync(ct),
                SalesOrderId = soLine.SalesOrderId,
                ShippingAddressId = po.DropShipAddressId ?? soLine.SalesOrder.ShippingAddressId,
                Status = ShipmentStatus.Delivered,
                FulfilmentType = FulfilmentType.DropShip,
                Carrier = Clip($"Vendor: {po.Vendor.CompanyName}", 100),
                TrackingNumber = tracking,
                ShippedDate = now,
                DeliveredDate = now,
                Notes = $"Drop-shipped by {po.Vendor.CompanyName} on purchase order {po.PONumber}.",
            };
            shipment.Lines.Add(new ShipmentLine
            {
                SalesOrderLineId = soLine.Id,
                PartId = soLine.PartId,
                Quantity = deliveredQuantity,
                Description = soLine.Description,
                InventoryRelievedAt = now,
            });
            shipment.ScanCode = ShipmentScanCode.Compute(shipment.ShipmentNumber, shipment.Lines);
            db.Shipments.Add(shipment);
            await db.SaveChangesAsync(ct);
            await identifiers.IssueAsync(BusinessEntityType.Shipment, shipment.Id, shipment.ShipmentNumber, ct);

            var order = await db.SalesOrders
                .Include(o => o.Lines).ThenInclude(l => l.Part)
                .Include(o => o.Shipments).ThenInclude(s => s.Lines)
                .FirstAsync(o => o.Id == soLine.SalesOrderId, ct);
            var closedLines = SalesOrderShipping.RecordShipped(
                order, await QuantityTolerances.ForSalesOrderAsync(db, order, ct));
            foreach (var (closedLine, closedQuantity) in closedLines)
                db.LogActivityAt(
                    "line-closed-within-tolerance",
                    $"Line {closedLine.LineNumber} closed within tolerance: {closedLine.ShippedQuantity:0.####} of {closedLine.Quantity:0.####} shipped, {closedQuantity:0.####} closed",
                    ("SalesOrder", order.Id));

            db.LogActivityAt(
                "delivered",
                $"Drop-ship shipment {shipment.ShipmentNumber}: {deliveredQuantity:0.####} delivered by {Clip(po.Vendor.CompanyName, 60)}",
                ("Shipment", shipment.Id));
            db.LogActivityAt(
                "drop-ship-delivered",
                $"Line {soLine.LineNumber}: {deliveredQuantity:0.####} delivered by the vendor on {po.PONumber} (shipment {shipment.ShipmentNumber})",
                ("SalesOrder", order.Id));
            db.LogActivityAt(
                "drop-ship-delivered",
                $"Vendor delivered {deliveredQuantity:0.####} to the customer (shipment {shipment.ShipmentNumber})",
                ("PurchaseOrder", po.Id));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await mediator.Publish(new ShipmentDeliveredEvent(shipment.Id, shipment.SalesOrderId, db.CurrentUserId ?? 0), ct);
    }

    public async Task<IReadOnlyList<DropShipStatusResponseModel>> GetPendingDropShipsAsync(CancellationToken ct)
    {
        var lines = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.SalesOrderLineId != null
                && l.PurchaseOrder.FulfilmentType == FulfilmentType.DropShip
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled
                && l.PurchaseOrder.Status != PurchaseOrderStatus.Closed
                && l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity > 0)
            .OrderBy(l => l.PurchaseOrder.ExpectedDeliveryDate).ThenBy(l => l.Id)
            .Select(l => new
            {
                Row = new DropShipStatusResponseModel
                {
                    SalesOrderId = l.SalesOrderLine!.SalesOrderId,
                    SalesOrderNumber = l.SalesOrderLine.SalesOrder.OrderNumber,
                    SalesOrderLineId = l.SalesOrderLine.Id,
                    CustomerName = l.SalesOrderLine.SalesOrder.Customer.Name,
                    PartNumber = l.Part != null ? l.Part.PartNumber : string.Empty,
                    PartDescription = l.Description,
                    Quantity = l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity,
                    VendorName = l.PurchaseOrder.Vendor.CompanyName,
                    PurchaseOrderId = l.PurchaseOrderId,
                    PurchaseOrderNumber = l.PurchaseOrder.PONumber,
                    PurchaseOrderLineId = l.Id,
                    ExpectedDeliveryDate = l.PurchaseOrder.ExpectedDeliveryDate,
                },
                PoStatus = l.PurchaseOrder.Status,
            })
            .ToListAsync(ct);
        return lines.Select(l => l.Row with { Status = l.PoStatus.ToString() }).ToList();
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
