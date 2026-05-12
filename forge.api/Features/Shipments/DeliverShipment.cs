using System.Security.Claims;

using MediatR;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Shipments;

public record DeliverShipmentCommand(int Id) : IRequest;

public class DeliverShipmentHandler(IShipmentRepository repo, IMediator mediator, IHttpContextAccessor httpContext)
    : IRequestHandler<DeliverShipmentCommand>
{
    public async Task Handle(DeliverShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status != ShipmentStatus.Shipped && shipment.Status != ShipmentStatus.InTransit)
            throw new InvalidOperationException("Only Shipped or InTransit shipments can be marked delivered");

        shipment.Status = ShipmentStatus.Delivered;
        shipment.DeliveredDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await mediator.Publish(
            new ShipmentDeliveredEvent(shipment.Id, shipment.SalesOrderId, userId), cancellationToken);
    }
}
