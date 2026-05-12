using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record RemoveShipmentPackageCommand(int ShipmentId, int PackageId) : IRequest;

public class RemoveShipmentPackageHandler(AppDbContext db) : IRequestHandler<RemoveShipmentPackageCommand>
{
    public async Task Handle(RemoveShipmentPackageCommand request, CancellationToken ct)
    {
        var package = await db.ShipmentPackages
            .FirstOrDefaultAsync(p => p.Id == request.PackageId && p.ShipmentId == request.ShipmentId, ct)
            ?? throw new KeyNotFoundException($"Package {request.PackageId} not found.");

        db.ShipmentPackages.Remove(package);
        await db.SaveChangesAsync(ct);
    }
}
