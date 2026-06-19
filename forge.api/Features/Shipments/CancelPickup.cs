using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipments;

public record CancelPickupCommand(int ShipmentId) : IRequest;

public class CancelPickupHandler(
    IShipmentRepository shipmentRepo,
    IShippingService shippingService,
    AppDbContext db)
    : IRequestHandler<CancelPickupCommand>
{
    public async Task Handle(CancelPickupCommand request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentRepo.FindWithDetailsAsync(request.ShipmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found");

        if (string.IsNullOrEmpty(shipment.PickupConfirmationNumber))
            throw new InvalidOperationException("No pickup is scheduled for this shipment.");

        var carrier = shipment.CarrierId is int cid
            ? await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken)
            : null;

        // Best-effort carrier cancel when there's a live integration; the local clear always happens so
        // the shipment isn't stuck showing a pickup the carrier already dropped.
        if (carrier?.IntegrationKind == CarrierIntegrationKind.Api
            && !string.IsNullOrWhiteSpace(carrier.IntegrationServiceId))
            await shippingService.CancelPickupAsync(
                shipment.PickupConfirmationNumber, carrier.IntegrationServiceId, cancellationToken);

        var prior = shipment.PickupConfirmationNumber;
        shipment.PickupConfirmationNumber = null;
        shipment.PickupScheduledDate = null;

        db.LogActivityAt("pickup-cancelled", $"Pickup {prior} cancelled", ("Shipment", shipment.Id));
        await shipmentRepo.SaveChangesAsync(cancellationToken);
    }
}
