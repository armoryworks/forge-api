using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Shipments;

public record UpdateShipmentCommand(
    int Id,
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes) : IRequest;

public class UpdateShipmentHandler(IShipmentRepository repo)
    : IRequestHandler<UpdateShipmentCommand>
{
    public async Task Handle(UpdateShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status == ShipmentStatus.Delivered || shipment.Status == ShipmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot update Delivered or Cancelled shipments");

        if (request.Carrier != null) shipment.Carrier = request.Carrier;
        if (request.TrackingNumber != null) shipment.TrackingNumber = request.TrackingNumber;
        if (request.ShippingCost.HasValue) shipment.ShippingCost = request.ShippingCost;
        if (request.Weight.HasValue) shipment.Weight = request.Weight;
        if (request.Notes != null) shipment.Notes = request.Notes;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
