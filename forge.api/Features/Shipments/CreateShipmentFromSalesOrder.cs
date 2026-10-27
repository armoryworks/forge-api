using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// One-click "create a shipment from this order" — builds a shipment covering every line that is
/// production-complete and not yet fully shipped, pre-filled with its remaining quantity, then delegates
/// to <see cref="CreateShipmentCommand"/> (which validates the confirmed order and the quantity still open
/// to ship, and stamps the scan code). A line is shippable when it has no production jobs, or all
/// of its jobs have reached a ship/complete stage — so un-produced lines are left behind, not shipped. Quantity a
/// vendor still has to drop-ship is left off too: it never leaves our dock.
/// </summary>
public record CreateShipmentFromSalesOrderCommand(int SalesOrderId) : IRequest<ShipmentListItemModel>;

public class CreateShipmentFromSalesOrderHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<CreateShipmentFromSalesOrderCommand, ShipmentListItemModel>
{
    // Mirrors the ship-ready definition in OnJobStageChanged_CheckShipReady so the notification and this
    // action agree on what "production complete" means.
    private static readonly HashSet<string> CompletionStageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shipped", "Invoiced/Sent", "Payment Received", "Completed"
    };

    public async Task<ShipmentListItemModel> Handle(CreateShipmentFromSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders
            .Include(o => o.Lines).ThenInclude(l => l.Jobs).ThenInclude(j => j.CurrentStage)
            .Include(o => o.Lines).ThenInclude(l => l.Part)
            .Include(o => o.Shipments).ThenInclude(s => s.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var lineIds = order.Lines.Select(l => l.Id).ToList();
        var dropShipOutstanding = (await db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.SalesOrderLineId != null && lineIds.Contains(l.SalesOrderLineId.Value)
                    && l.PurchaseOrder.FulfilmentType == FulfilmentType.DropShip
                    && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled)
                .Select(l => new { LineId = l.SalesOrderLineId!.Value, Open = l.OrderedQuantity - l.ReceivedQuantity - l.CancelledShortCloseQuantity })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.LineId)
            .ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0m, l.Open)));

        var lines = new List<CreateShipmentLineModel>();
        foreach (var line in order.Lines)
        {
            var open = SalesOrderShipping.OpenToShip(order, line) - dropShipOutstanding.GetValueOrDefault(line.Id);
            if (open <= 0 || !line.ShipsGoods) continue;

            // Ship a line only once its production is done: no jobs, or every job at a ship/complete stage.
            var productionComplete = line.Jobs.Count == 0 || line.Jobs.All(j =>
                j.CurrentStage != null
                && (CompletionStageNames.Contains(j.CurrentStage.Name)
                    || j.CurrentStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase)));

            if (productionComplete)
                lines.Add(new CreateShipmentLineModel(line.Id, open, null));
        }

        if (lines.Count == 0)
            throw new InvalidOperationException(
                "No lines are ready to ship — production is incomplete or everything is already shipped.");

        return await mediator.Send(new CreateShipmentCommand(
            order.Id, ShippingAddressId: null, Carrier: null, TrackingNumber: null,
            ShippingCost: null, Weight: null, Notes: null, Lines: lines), cancellationToken);
    }
}
