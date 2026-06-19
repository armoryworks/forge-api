using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// One-click "create a shipment from this order" — builds a shipment covering every line that is
/// production-complete and not yet fully shipped, pre-filled with its remaining quantity, then delegates
/// to <see cref="CreateShipmentCommand"/> (which validates the confirmed order, decrements fulfillment,
/// pushes SO status, and stamps the scan code). A line is shippable when it has no production jobs, or all
/// of its jobs have reached a ship/complete stage — so un-produced lines are left behind, not shipped.
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
            .FirstOrDefaultAsync(o => o.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found");

        var lines = new List<CreateShipmentLineModel>();
        foreach (var line in order.Lines)
        {
            if (line.RemainingQuantity <= 0) continue;

            // Ship a line only once its production is done: no jobs, or every job at a ship/complete stage.
            var productionComplete = line.Jobs.Count == 0 || line.Jobs.All(j =>
                j.CurrentStage != null
                && (CompletionStageNames.Contains(j.CurrentStage.Name)
                    || j.CurrentStage.Name.Contains("Ship", StringComparison.OrdinalIgnoreCase)));

            if (productionComplete)
                lines.Add(new CreateShipmentLineModel(line.Id, line.RemainingQuantity, null));
        }

        if (lines.Count == 0)
            throw new InvalidOperationException(
                "No lines are ready to ship — production is incomplete or everything is already shipped.");

        return await mediator.Send(new CreateShipmentCommand(
            order.Id, ShippingAddressId: null, Carrier: null, TrackingNumber: null,
            ShippingCost: null, Weight: null, Notes: null, Lines: lines), cancellationToken);
    }
}
