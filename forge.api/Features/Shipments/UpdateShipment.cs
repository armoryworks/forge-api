using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Shipments;

public record UpdateShipmentCommand(
    int Id,
    string? Carrier,
    string? TrackingNumber,
    decimal? ShippingCost,
    decimal? Weight,
    string? Notes,
    int? ShippingAddressId = null) : IRequest;

/// <summary>
/// Corrects/adjusts a shipment's details (ship-to address, carrier, tracking, cost, weight, notes).
/// Every change is captured as a rollup <c>ActivityLog</c> row on the shipment so the correction is
/// auditable on the Activity tab. Delivered/Cancelled shipments are immutable.
/// </summary>
public class UpdateShipmentHandler(IShipmentRepository repo, AppDbContext db)
    : IRequestHandler<UpdateShipmentCommand>
{
    public async Task Handle(UpdateShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot update Delivered or Cancelled shipments");

        var changedFields = new List<string>();

        if (request.ShippingAddressId.HasValue && request.ShippingAddressId != shipment.ShippingAddressId)
        {
            // The address must belong to this shipment's customer — a clean 409 instead of an FK 500,
            // and it prevents shipping to another customer's address.
            var customerId = shipment.SalesOrder.CustomerId;
            var address = await db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.ShippingAddressId && a.CustomerId == customerId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "The selected ship-to address does not belong to this shipment's customer.");
            shipment.ShippingAddressId = address.Id;
            changedFields.Add("shippingAddress");
        }

        if (request.Carrier != null && request.Carrier != shipment.Carrier)
        {
            shipment.Carrier = request.Carrier;
            changedFields.Add("carrier");
        }

        if (request.TrackingNumber != null && request.TrackingNumber != shipment.TrackingNumber)
        {
            shipment.TrackingNumber = request.TrackingNumber;
            changedFields.Add("trackingNumber");
        }

        if (request.ShippingCost.HasValue && request.ShippingCost != shipment.ShippingCost)
        {
            shipment.ShippingCost = request.ShippingCost;
            changedFields.Add("shippingCost");
        }

        if (request.Weight.HasValue && request.Weight != shipment.Weight)
        {
            shipment.Weight = request.Weight;
            changedFields.Add("weight");
        }

        if (request.Notes != null && request.Notes != shipment.Notes)
        {
            shipment.Notes = request.Notes;
            changedFields.Add("notes");
        }

        if (changedFields.Count == 0)
            return;

        // One rollup activity row per save — the Activity tab is the audit trail for corrections.
        db.LogActivityAt(
            "updated",
            changedFields.Count == 1
                ? $"Updated shipment {shipment.ShipmentNumber}: {changedFields[0]}"
                : $"Updated {changedFields.Count} fields on shipment {shipment.ShipmentNumber}: {string.Join(", ", changedFields)}",
            ("Shipment", shipment.Id));

        await repo.SaveChangesAsync(cancellationToken);
    }
}
