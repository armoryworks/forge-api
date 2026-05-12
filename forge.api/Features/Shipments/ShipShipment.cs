using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Shipments;

public record ShipShipmentCommand(int Id) : IRequest;

public class ShipShipmentHandler(IShipmentRepository repo)
    : IRequestHandler<ShipShipmentCommand>
{
    public async Task Handle(ShipShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status != ShipmentStatus.Pending && shipment.Status != ShipmentStatus.Packed)
            throw new InvalidOperationException("Only Pending or Packed shipments can be shipped");

        shipment.Status = ShipmentStatus.Shipped;
        shipment.ShippedDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
