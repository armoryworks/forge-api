using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GetShipmentPackagesQuery(int ShipmentId) : IRequest<List<ShipmentPackageResponseModel>>;

public class GetShipmentPackagesHandler(AppDbContext db) : IRequestHandler<GetShipmentPackagesQuery, List<ShipmentPackageResponseModel>>
{
    public async Task<List<ShipmentPackageResponseModel>> Handle(GetShipmentPackagesQuery request, CancellationToken ct)
    {
        return await db.ShipmentPackages
            .Where(p => p.ShipmentId == request.ShipmentId)
            .OrderBy(p => p.Id)
            .Select(p => new ShipmentPackageResponseModel(
                p.Id, p.ShipmentId, p.TrackingNumber, p.Carrier,
                p.Weight, p.Length, p.Width, p.Height, p.Status))
            .ToListAsync(ct);
    }
}
