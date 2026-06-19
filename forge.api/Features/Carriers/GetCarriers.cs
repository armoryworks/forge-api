using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Carriers;

public record GetCarriersQuery(bool ActiveOnly = true) : IRequest<List<CarrierListItemModel>>;

public class GetCarriersHandler(AppDbContext db) : IRequestHandler<GetCarriersQuery, List<CarrierListItemModel>>
{
    public async Task<List<CarrierListItemModel>> Handle(GetCarriersQuery request, CancellationToken cancellationToken)
    {
        var query = db.Carriers.AsNoTracking();
        if (request.ActiveOnly)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CarrierListItemModel(
                c.Id, c.Name, c.Code, c.Scac,
                c.IntegrationKind.ToString(), c.DeliveryUpdateMode.ToString(),
                c.IntegrationServiceId, c.RequiresScanToShip, c.IsActive, c.SortOrder,
                c.CredentialClientId != null && c.CredentialSecret != null,
                c.CredentialClientId,
                c.CredentialEnvironment))
            .ToListAsync(cancellationToken);
    }
}
