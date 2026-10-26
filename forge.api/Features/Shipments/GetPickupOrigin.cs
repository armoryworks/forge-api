using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipments;

public record GetPickupOriginQuery : IRequest<PickupOriginResponseModel>;

public class GetPickupOriginHandler(AppDbContext db) : IRequestHandler<GetPickupOriginQuery, PickupOriginResponseModel>
{
    public async Task<PickupOriginResponseModel> Handle(GetPickupOriginQuery request, CancellationToken cancellationToken)
    {
        var origin = await db.CompanyLocations.AsNoTracking()
            .Where(l => l.IsDefault && l.IsActive)
            .Select(l => new { l.Id, l.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (origin is null) return new PickupOriginResponseModel(null, null);

        var timeZone = await db.Plants.AsNoTracking()
            .Where(p => p.CompanyLocationId == origin.Id && p.IsActive && p.TimeZone != null && p.TimeZone != "")
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Id)
            .Select(p => p.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        return new PickupOriginResponseModel(origin.Name, timeZone);
    }
}
