using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GetShippingRatesQuery(int ShipmentId) : IRequest<List<ShippingRate>>;

public class GetShippingRatesValidator : AbstractValidator<GetShippingRatesQuery>
{
    public GetShippingRatesValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
    }
}

/// <summary>
/// Rate-shops a shipment against every configured carrier. Addresses + packages are derived from the
/// shipment itself (same resolution <see cref="CreateShippingLabelHandler"/> uses) so the UI only needs
/// the shipment id — the from-address is the company's default location, the to-address is the shipment's
/// shipping address. Throws a 409 with a user-readable message when a prerequisite (shipping address /
/// default company location) is missing, mirroring label creation so the two steps fail the same way.
/// </summary>
public class GetShippingRatesHandler(
    IShipmentRepository shipmentRepo,
    IShippingService shippingService,
    AppDbContext db)
    : IRequestHandler<GetShippingRatesQuery, List<ShippingRate>>
{
    public async Task<List<ShippingRate>> Handle(GetShippingRatesQuery request, CancellationToken cancellationToken)
    {
        var shipment = await shipmentRepo.FindWithDetailsAsync(request.ShipmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found");

        var shippingAddress = shipment.ShippingAddress
            ?? throw new InvalidOperationException("Shipment has no shipping address assigned");

        var origin = await db.CompanyLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IsDefault && l.IsActive, cancellationToken)
            ?? throw new InvalidOperationException(
                "No default company location is configured — set one before rate-shopping a shipment.");

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
            packages.Add(new ShippingPackage(
                shipment.Weight ?? ShipmentWeight.Derive(shipment) ?? 1m,
                shipment.Length ?? 10m, shipment.Width ?? 10m, shipment.Height ?? 10m));

        var shipmentRequest = new ShipmentRequest(fromAddress, toAddress, packages, null);
        return await shippingService.GetRatesAsync(shipmentRequest, cancellationToken);
    }
}
