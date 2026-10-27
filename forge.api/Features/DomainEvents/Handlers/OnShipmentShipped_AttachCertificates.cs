using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality.Coa;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

/// <summary>
/// CAP-QC-COA — when a shipment ships to a customer marked as requiring a certificate of analysis, issue and
/// attach one for every lot it carried. A lot that is out of specification or has no results is not
/// certified; the shipment's activity log records it so quality can follow up.
/// </summary>
public class OnShipmentShipped_AttachCertificates(
    AppDbContext db,
    ICapabilitySnapshotProvider capabilities,
    IMediator mediator,
    ILogger<OnShipmentShipped_AttachCertificates> logger)
    : INotificationHandler<ShipmentShippedEvent>
{
    public async Task Handle(ShipmentShippedEvent notification, CancellationToken ct)
    {
        if (!capabilities.IsEnabled("CAP-QC-COA"))
            return;

        var requiresCoa = await db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == notification.SalesOrderId)
            .Select(o => o.Customer.RequiresCoa)
            .FirstOrDefaultAsync(ct);
        if (!requiresCoa)
            return;

        var result = await mediator.Send(new AttachShipmentCertificatesCommand(
            notification.ShipmentId, new AttachShipmentCertificatesRequestModel(), notification.UserId == 0 ? null : notification.UserId), ct);

        foreach (var warning in result.Warnings)
        {
            logger.LogWarning(
                "Certificate of analysis not attached to shipment {ShipmentId} for lot {LotNumber}: {Code}",
                notification.ShipmentId, warning.LotNumber, warning.Code);
            db.ActivityLogs.Add(new ActivityLog
            {
                EntityType = "Shipment",
                EntityId = notification.ShipmentId,
                UserId = notification.UserId == 0 ? null : notification.UserId,
                Action = "coa-not-attached",
                Description = warning.Code == CertificateOfAnalysisIssuer.NonConformingCode
                    ? $"No certificate of analysis attached for lot {warning.LotNumber}: out of specification ({string.Join(", ", warning.FailedCharacteristics)})."
                    : $"No certificate of analysis attached for lot {warning.LotNumber}: no inspection or SPC results.",
            });
        }
        if (result.Warnings.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
