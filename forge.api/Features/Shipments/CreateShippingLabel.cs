using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Shipments;

public record CreateShippingLabelCommand(int ShipmentId, string CarrierId) : IRequest<ShippingLabel>;

public class CreateShippingLabelValidator : AbstractValidator<CreateShippingLabelCommand>
{
    public CreateShippingLabelValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
        RuleFor(x => x.CarrierId).NotEmpty().WithMessage("CarrierId is required");
    }
}

public class CreateShippingLabelHandler(
    IShipmentRepository shipmentRepo,
    IShippingService shippingService)
    : IRequestHandler<CreateShippingLabelCommand, ShippingLabel>
{
    public async Task<ShippingLabel> Handle(CreateShippingLabelCommand request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentRepo.FindWithDetailsAsync(request.ShipmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found");

        var shippingAddress = shipment.ShippingAddress
            ?? throw new InvalidOperationException("Shipment has no shipping address assigned");

        // Build from address from system settings (company address)
        // For now, use a placeholder — real implementation would pull from SystemSettings
        var fromAddress = new ShippingAddress(
            "Warehouse",
            "123 Warehouse St",
            "City",
            "ST",
            "00000",
            "US");

        var toAddress = new ShippingAddress(
            shippingAddress.Label,
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
        var label = await shippingService.CreateLabelAsync(shipmentRequest, request.CarrierId, cancellationToken);

        // Update shipment with tracking info
        shipment.TrackingNumber = label.TrackingNumber;
        shipment.Carrier = label.CarrierName;
        await shipmentRepo.SaveChangesAsync(cancellationToken);

        return label;
    }
}
