using System.Globalization;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipments;

public record CancelShipmentCommand(int Id) : IRequest;

public class CancelShipmentHandler(AppDbContext db) : IRequestHandler<CancelShipmentCommand>
{
    public async Task Handle(CancelShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments
            .Include(s => s.Lines)
            .Include(s => s.AssignedCarrier)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status is not (ShipmentStatus.Pending or ShipmentStatus.Packed))
            throw new InvalidOperationException("Only Pending or Packed shipments can be cancelled.");

        if (shipment.AssignedCarrier?.IntegrationKind == CarrierIntegrationKind.Api
            && !string.IsNullOrWhiteSpace(shipment.TrackingNumber))
            throw new InvalidOperationException(
                "A shipping label has been bought for this shipment. Void the label with the carrier before cancelling.");

        if (!string.IsNullOrEmpty(shipment.PickupConfirmationNumber))
            throw new InvalidOperationException(
                "A carrier pickup is scheduled for this shipment. Cancel the pickup before cancelling the shipment.");

        var invoiceNumber = await db.Invoices
            .Where(i => i.ShipmentId == shipment.Id && i.Status != InvoiceStatus.Voided)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoiceNumber is not null)
            throw new InvalidOperationException(
                $"Invoice {invoiceNumber} bills this shipment. Void the invoice before cancelling the shipment.");

        var order = await db.SalesOrders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Part)
            .FirstAsync(o => o.Id == shipment.SalesOrderId, cancellationToken);

        foreach (var line in shipment.Lines.Where(l => l.SalesOrderLineId.HasValue))
        {
            var orderLine = order.Lines.FirstOrDefault(l => l.Id == line.SalesOrderLineId);
            if (orderLine is not null)
                orderLine.ShippedQuantity = Math.Max(0m, orderLine.ShippedQuantity - line.Quantity);
        }

        if (order.Status is SalesOrderStatus.Shipped or SalesOrderStatus.PartiallyShipped)
        {
            if (order.Lines.Any(l => l.ShipsGoods) && order.Lines.Where(l => l.ShipsGoods).All(l => l.IsFullyShipped))
                order.Status = SalesOrderStatus.Shipped;
            else if (order.Lines.Any(l => l.ShippedQuantity > 0))
                order.Status = SalesOrderStatus.PartiallyShipped;
            else
                order.Status = SalesOrderStatus.Confirmed;
        }

        shipment.Status = ShipmentStatus.Cancelled;

        db.LogActivityAt(
            "shipment-cancelled",
            $"Shipment {shipment.ShipmentNumber} cancelled; {shipment.Lines.Sum(l => l.Quantity).ToString("0.####", CultureInfo.InvariantCulture)} unit(s) returned to the order",
            ("Shipment", shipment.Id),
            ("SalesOrder", order.Id));
        await db.SaveChangesAsync(cancellationToken);
    }
}
