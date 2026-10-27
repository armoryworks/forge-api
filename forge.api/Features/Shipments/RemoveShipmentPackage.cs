using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

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
        db.LogActivityAt("package-removed", $"Package removed{TrackingSuffix(package.TrackingNumber)}", ("Shipment", package.ShipmentId));
        await db.SaveChangesAsync(ct);
    }

    private static string TrackingSuffix(string? tracking) => string.IsNullOrWhiteSpace(tracking) ? string.Empty : $" (tracking {tracking})";
}
