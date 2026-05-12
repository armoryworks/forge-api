using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record AddShipmentPackageCommand(
    int ShipmentId,
    string? TrackingNumber,
    string? Carrier,
    decimal? Weight,
    decimal? Length,
    decimal? Width,
    decimal? Height) : IRequest<ShipmentPackageResponseModel>;

public class AddShipmentPackageValidator : AbstractValidator<AddShipmentPackageCommand>
{
    public AddShipmentPackageValidator()
    {
        RuleFor(x => x.ShipmentId).GreaterThan(0);
    }
}

public class AddShipmentPackageHandler(AppDbContext db) : IRequestHandler<AddShipmentPackageCommand, ShipmentPackageResponseModel>
{
    public async Task<ShipmentPackageResponseModel> Handle(AddShipmentPackageCommand request, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == request.ShipmentId, ct)
            ?? throw new KeyNotFoundException($"Shipment {request.ShipmentId} not found.");

        var package = new ShipmentPackage
        {
            ShipmentId = request.ShipmentId,
            TrackingNumber = request.TrackingNumber,
            Carrier = request.Carrier,
            Weight = request.Weight,
            Length = request.Length,
            Width = request.Width,
            Height = request.Height,
        };

        db.ShipmentPackages.Add(package);
        await db.SaveChangesAsync(ct);

        return new ShipmentPackageResponseModel(
            package.Id, package.ShipmentId, package.TrackingNumber, package.Carrier,
            package.Weight, package.Length, package.Width, package.Height, package.Status);
    }
}
