using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

/// <summary>
/// Provider-agnostic inbound tracking update — the normalized shape a carrier webhook is mapped to.
/// Finds the shipment by tracking number and, when the carrier is configured for
/// <see cref="CarrierDeliveryUpdateMode.Webhook"/> delivery updates and the status is delivered, marks
/// it Delivered (via <see cref="DeliverShipmentCommand"/>). Returns whether it transitioned the shipment.
/// Best-effort: unknown tracking numbers and non-webhook carriers are logged and ignored, not errored —
/// a webhook sender expects a quick 200.
/// </summary>
public record IngestTrackingUpdateCommand(string TrackingNumber, string Status) : IRequest<bool>;

public class IngestTrackingUpdateHandler(
    AppDbContext db,
    IMediator mediator,
    ILogger<IngestTrackingUpdateHandler> logger)
    : IRequestHandler<IngestTrackingUpdateCommand, bool>
{
    public async Task<bool> Handle(IngestTrackingUpdateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            logger.LogWarning("Tracking webhook received with no tracking number; ignoring");
            return false;
        }

        var shipment = await db.Shipments
            .Include(s => s.AssignedCarrier)
            .FirstOrDefaultAsync(s => s.TrackingNumber == request.TrackingNumber, cancellationToken);

        if (shipment is null)
        {
            logger.LogWarning("Tracking webhook: no shipment for tracking number {TrackingNumber}", request.TrackingNumber);
            return false;
        }

        // Only carriers configured to receive delivery via webhook act on a push — Poll/Manual carriers
        // ignore inbound webhooks (the sweep job or a user owns their delivery transition).
        if (shipment.AssignedCarrier?.DeliveryUpdateMode != CarrierDeliveryUpdateMode.Webhook)
            return false;

        if (!ShipmentDeliveryStatus.IsDelivered(request.Status))
            return false;

        // Idempotent: a re-delivered or terminal shipment is a no-op (DeliverShipment would also reject it).
        if (shipment.Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
            return false;

        await mediator.Send(new DeliverShipmentCommand(shipment.Id), cancellationToken);
        logger.LogInformation(
            "Tracking webhook marked shipment {ShipmentNumber} delivered (tracking {TrackingNumber})",
            shipment.ShipmentNumber, request.TrackingNumber);
        return true;
    }
}
