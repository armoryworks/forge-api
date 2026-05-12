using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record UpdateShipmentPackageCommand(
    int ShipmentId,
    int PackageId,
    string? TrackingNumber,
    string? Carrier,
    decimal? Weight,
    string? Status) : IRequest<ShipmentPackageResponseModel>;

public class UpdateShipmentPackageHandler(AppDbContext db) : IRequestHandler<UpdateShipmentPackageCommand, ShipmentPackageResponseModel>
{
    public async Task<ShipmentPackageResponseModel> Handle(UpdateShipmentPackageCommand request, CancellationToken ct)
    {
        var package = await db.ShipmentPackages
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && p.ShipmentId == request.ShipmentId, ct)
            ?? throw new KeyNotFoundException($"Package {request.PackageId} not found.");

        if (request.TrackingNumber != null) package.TrackingNumber = request.TrackingNumber;
        if (request.Carrier != null) package.Carrier = request.Carrier;
        if (request.Weight.HasValue) package.Weight = request.Weight;
        if (request.Status != null) package.Status = request.Status;

        await db.SaveChangesAsync(ct);

        return new ShipmentPackageResponseModel(
            package.Id, package.ShipmentId, package.TrackingNumber, package.Carrier,
            package.Weight, package.Length, package.Width, package.Height, package.Status);
    }
}
