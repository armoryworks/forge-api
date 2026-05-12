using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesOrders;

public record ConfirmDropShipDeliveryCommand(int PurchaseOrderId, int PurchaseOrderLineId, ConfirmDropShipDeliveryRequestModel Request) : IRequest;

public class ConfirmDropShipDeliveryHandler(AppDbContext db, IDropShipService dropShipService) : IRequestHandler<ConfirmDropShipDeliveryCommand>
{
    public async Task Handle(ConfirmDropShipDeliveryCommand command, CancellationToken cancellationToken)
    {
        // Verify the PO line exists
        var poLine = await db.PurchaseOrderLines
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == command.PurchaseOrderLineId && l.PurchaseOrderId == command.PurchaseOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Purchase order line {command.PurchaseOrderLineId} not found");

        await dropShipService.ConfirmDropShipDeliveryAsync(
            command.PurchaseOrderLineId,
            command.Request.DeliveredQuantity,
            command.Request.TrackingNumber,
            cancellationToken);
    }
}
