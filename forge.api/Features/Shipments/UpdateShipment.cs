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
    int? ShippingAddressId = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null,
    // Optional caller-supplied shipment number — editable only before the shipment
    // has shipped, gated by shipments.allow_manual_numbers.
    string? ShipmentNumber = null) : IRequest;

/// <summary>
/// Corrects/adjusts a shipment's details (ship-to address, carrier, tracking, cost, weight, notes).
/// Every change is captured as a rollup <c>ActivityLog</c> row on the shipment so the correction is
/// auditable on the Activity tab. Delivered/Cancelled shipments are immutable.
/// </summary>
public class UpdateShipmentHandler(
    IShipmentRepository repo,
    ISystemSettingRepository systemSettings,
    IBusinessIdentifierService identifiers,
    AppDbContext db)
    : IRequestHandler<UpdateShipmentCommand>
{
    // System setting that gates caller-supplied shipment numbers (shared with CreateShipment).
    private const string AllowManualShipmentNumbersKey = "shipments.allow_manual_numbers";

    public async Task Handle(UpdateShipmentCommand request, CancellationToken cancellationToken)
    {
        var shipment = await repo.FindWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.Id} not found");

        if (shipment.Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot update Delivered or Cancelled shipments");

        var changedFields = new List<string>();

        // User-settable shipment number — only before the shipment has shipped (Pending/Packed),
        // manual numbers enabled, and unique (excluding this shipment). Registry records the rename.
        if (request.ShipmentNumber is not null)
        {
            var newNumber = request.ShipmentNumber.Trim();
            if (newNumber.Length > 0 && !string.Equals(newNumber, shipment.ShipmentNumber, StringComparison.Ordinal))
            {
                if (shipment.Status is not (ShipmentStatus.Pending or ShipmentStatus.Packed))
                    throw new InvalidOperationException(
                        "A shipment number can only be changed before the shipment has shipped.");
                if (!await ManualShipmentNumbersAllowedAsync(cancellationToken))
                    throw new InvalidOperationException(
                        "Manual shipment numbers are disabled. Turn on 'shipments.allow_manual_numbers' in settings to change a shipment number.");
                if (await repo.ShipmentNumberExistsAsync(newNumber, shipment.Id, cancellationToken))
                    throw new InvalidOperationException($"Shipment number '{newNumber}' is already in use.");
                await identifiers.IssueAsync(BusinessEntityType.Shipment, shipment.Id, shipment.ShipmentNumber, cancellationToken);
                await identifiers.RenameAsync(BusinessEntityType.Shipment, shipment.Id, newNumber, cancellationToken);
                shipment.ShipmentNumber = newNumber;
                changedFields.Add("shipmentNumber");
            }
        }

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

        if (request.Length.HasValue && request.Length != shipment.Length)
        {
            shipment.Length = request.Length;
            changedFields.Add("length");
        }

        if (request.Width.HasValue && request.Width != shipment.Width)
        {
            shipment.Width = request.Width;
            changedFields.Add("width");
        }

        if (request.Height.HasValue && request.Height != shipment.Height)
        {
            shipment.Height = request.Height;
            changedFields.Add("height");
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

    private async Task<bool> ManualShipmentNumbersAllowedAsync(CancellationToken ct)
    {
        var setting = await systemSettings.FindByKeyAsync(AllowManualShipmentNumbersKey, ct);
        return setting is not null && bool.TryParse(setting.Value, out var enabled) && enabled;
    }
}
