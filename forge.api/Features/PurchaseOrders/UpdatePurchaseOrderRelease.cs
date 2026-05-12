using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.PurchaseOrders;

public record UpdatePurchaseOrderReleaseCommand(int PurchaseOrderId, int ReleaseNumber, UpdatePurchaseOrderReleaseRequestModel Request) : IRequest;

public class UpdatePurchaseOrderReleaseHandler(AppDbContext db) : IRequestHandler<UpdatePurchaseOrderReleaseCommand>
{
    public async Task Handle(UpdatePurchaseOrderReleaseCommand request, CancellationToken cancellationToken)
    {
        var release = await db.PurchaseOrderReleases
            .Include(r => r.PurchaseOrder)
            .FirstOrDefaultAsync(r => r.PurchaseOrderId == request.PurchaseOrderId && r.ReleaseNumber == request.ReleaseNumber, cancellationToken)
            ?? throw new KeyNotFoundException($"Release #{request.ReleaseNumber} not found on PO {request.PurchaseOrderId}");

        var oldQuantity = release.Quantity;

        if (request.Request.Quantity.HasValue) release.Quantity = request.Request.Quantity.Value;
        if (request.Request.RequestedDeliveryDate.HasValue) release.RequestedDeliveryDate = request.Request.RequestedDeliveryDate.Value;
        if (request.Request.ActualDeliveryDate.HasValue) release.ActualDeliveryDate = request.Request.ActualDeliveryDate.Value;
        if (request.Request.Status.HasValue) release.Status = request.Request.Status.Value;
        if (request.Request.Notes is not null) release.Notes = request.Request.Notes;

        // Update blanket released quantity if quantity changed
        if (request.Request.Quantity.HasValue && request.Request.Quantity.Value != oldQuantity)
        {
            var po = release.PurchaseOrder;
            po.BlanketReleasedQuantity = (po.BlanketReleasedQuantity ?? 0) - oldQuantity + request.Request.Quantity.Value;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
