using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Shipments;
using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Carrier delivery automation (poll mode). Every run finds Shipped/InTransit shipments whose assigned
/// carrier is configured for <see cref="CarrierDeliveryUpdateMode.Poll"/> and that carry a tracking
/// number, asks the <c>IShippingService</c> abstraction for current tracking, and — when the carrier
/// reports delivered — marks the shipment Delivered via <see cref="DeliverShipmentCommand"/> (reusing
/// its gating + the ShipmentDeliveredEvent). Carriers set to Manual or Webhook are skipped here; the
/// webhook path handles the latter. Provider-agnostic: the job never knows which carrier API answers.
/// </summary>
public class ShipmentDeliverySweepJob(
    AppDbContext db,
    Forge.Core.Interfaces.IShippingService shipping,
    IMediator mediator,
    ILogger<ShipmentDeliverySweepJob> logger)
{
    /// <summary>Cap per run — a large in-transit book drains over successive sweeps, not in one bite.</summary>
    public const int MaxPerSweep = 100;

    public async Task SweepAsync(CancellationToken ct)
    {
        var candidates = await db.Shipments.AsNoTracking()
            .Where(s => (s.Status == ShipmentStatus.Shipped || s.Status == ShipmentStatus.InTransit)
                && s.TrackingNumber != null
                && s.AssignedCarrier != null
                && s.AssignedCarrier.DeliveryUpdateMode == CarrierDeliveryUpdateMode.Poll)
            .OrderBy(s => s.Id)
            .Take(MaxPerSweep)
            .Select(s => new { s.Id, s.TrackingNumber })
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var s in candidates)
        {
            try
            {
                var tracking = await shipping.GetTrackingAsync(s.TrackingNumber!, ct);
                if (tracking is null || !ShipmentDeliveryStatus.IsDelivered(tracking.Status)) continue;

                await mediator.Send(new DeliverShipmentCommand(s.Id), ct);
                delivered++;
            }
            catch (Exception ex)
            {
                // One bad tracking call (carrier 5xx, transient) must not abort the sweep.
                logger.LogWarning(ex,
                    "ShipmentDeliverySweepJob: tracking poll failed for shipment {ShipmentId}", s.Id);
            }
        }

        if (delivered > 0)
            logger.LogInformation(
                "ShipmentDeliverySweepJob: marked {Count} shipment(s) delivered from {Polled} polled",
                delivered, candidates.Count);
    }
}
