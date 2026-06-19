using System.Security.Claims;

using MediatR;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record DeliverShipmentCommand(int Id) : IRequest;

public class DeliverShipmentHandler(
    IShipmentRepository repo,
    IMediator mediator,
    // Optional / null-default: the HTTP path supplies it (delivery marked by a user); the delivery
    // automation (Hangfire poll / inbound webhook) runs outside any request, so a null context resolves
    // to the system principal (userId 0) rather than throwing. db backs the delivered activity-log row.
    IHttpContextAccessor? httpContext = null,
    AppDbContext? db = null)
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

        var userId = int.TryParse(
            httpContext?.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;

        // Transactional entity logs on itself; same scoped context as the repo, so it flushes below.
        db?.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "Shipment",
            EntityId = shipment.Id,
            UserId = userId == 0 ? null : userId,
            Action = "delivered",
            Description = $"Shipment {shipment.ShipmentNumber} marked delivered.",
        });

        await repo.SaveChangesAsync(cancellationToken);

        await mediator.Publish(
            new ShipmentDeliveredEvent(shipment.Id, shipment.SalesOrderId, userId), cancellationToken);
    }
}
