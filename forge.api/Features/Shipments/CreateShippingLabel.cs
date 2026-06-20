using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record CreateShippingLabelCommand(int ShipmentId, string? CarrierId = null) : IRequest<ShippingLabel>;

public class CreateShippingLabelValidator : AbstractValidator<CreateShippingLabelCommand>
{
    public CreateShippingLabelValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
        // CarrierId is optional — when omitted, the shipment's assigned carrier supplies it (resolved
        // in the handler, which 409s if neither is present or the carrier has no live integration).
    }
}

public class CreateShippingLabelHandler(
    IShipmentRepository shipmentRepo,
    IShippingService shippingService,
    IStorageService storage,
    AppDbContext db)
    : IRequestHandler<CreateShippingLabelCommand, ShippingLabel>
{
    public async Task<ShippingLabel> Handle(CreateShippingLabelCommand request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentRepo.FindWithDetailsAsync(request.ShipmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found");

        var shippingAddress = shipment.ShippingAddress
            ?? throw new InvalidOperationException("Shipment has no shipping address assigned");

        // Carrier-entity wiring: an explicit CarrierId overrides; otherwise resolve the integration from
        // the shipment's assigned carrier. Only carriers with a live (Api) integration can mint a label —
        // Manual / custom shippers record their tracking number by hand.
        var carrierId = request.CarrierId;
        if (string.IsNullOrWhiteSpace(carrierId))
        {
            var carrier = shipment.CarrierId is int cid
                ? await db.Carriers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken)
                : null;
            if (carrier is null)
                throw new InvalidOperationException(
                    "No carrier is assigned to this shipment — assign one (or pass a carrierId) before creating a label.");
            if (carrier.IntegrationKind != CarrierIntegrationKind.Api
                || string.IsNullOrWhiteSpace(carrier.IntegrationServiceId))
                throw new InvalidOperationException(
                    $"Carrier {carrier.Name} has no live integration — record its tracking number manually.");
            carrierId = carrier.IntegrationServiceId;
        }

        // From-address: the company's default location (was a hardcoded placeholder — a real label needs a
        // real origin). Required before a label can be minted.
        var origin = await db.CompanyLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, cancellationToken)
            ?? throw new InvalidOperationException(
                "No default company location is configured — set one before creating a shipping label.");

        var fromAddress = new ShippingAddress(
            origin.Name, origin.Line1, origin.City, origin.State, origin.PostalCode, origin.Country);

        var toAddress = new ShippingAddress(
            shipment.SalesOrder.Customer.GetDisplayName(),
            shippingAddress.Line1,
            shippingAddress.City,
            shippingAddress.State,
            shippingAddress.PostalCode,
            shippingAddress.Country);

        var packages = shipment.Packages.Select(p => new ShippingPackage(
            p.Weight ?? 1m,
            p.Length ?? 10m,
            p.Width ?? 10m,
            p.Height ?? 10m)).ToList();

        if (packages.Count == 0)
            packages.Add(new ShippingPackage(shipment.Weight ?? 1m, 10m, 10m, 10m));

        var shipmentRequest = new ShipmentRequest(fromAddress, toAddress, packages, null);
        var label = await shippingService.CreateLabelAsync(shipmentRequest, carrierId!, cancellationToken);

        // Persist the carrier's tracking number; the delivery automation (poll/webhook) picks it up from here.
        shipment.TrackingNumber = label.TrackingNumber;
        shipment.Carrier = label.CarrierName;
        await shipmentRepo.SaveChangesAsync(cancellationToken);

        // Stash the raw carrier label PNG so the combined ship document can be (re)generated on demand —
        // the carrier returns the label only once, here at creation.
        if (label.LabelBytes is { Length: > 0 } labelBytes)
        {
            await storage.EnsureBucketExistsAsync(ShipLabelStorage.Bucket, cancellationToken);
            using var ms = new MemoryStream(labelBytes);
            await storage.UploadAsync(ShipLabelStorage.Bucket, ShipLabelStorage.Key(shipment.Id), ms, "image/png", cancellationToken);
        }

        return label;
    }
}
