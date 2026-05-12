using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record GetCompanyLocationsQuery : IRequest<List<CompanyLocationResponseModel>>;

public class GetCompanyLocationsHandler(AppDbContext db)
    : IRequestHandler<GetCompanyLocationsQuery, List<CompanyLocationResponseModel>>
{
    public async Task<List<CompanyLocationResponseModel>> Handle(
        GetCompanyLocationsQuery request, CancellationToken ct)
    {
        var locations = await db.CompanyLocations
            .AsNoTracking()
            .OrderByDescending(l => l.IsDefault)
            .ThenBy(l => l.Name)
            .ToListAsync(ct);

        return locations.Select(l => new CompanyLocationResponseModel(
            l.Id, l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode,
            l.Country, l.Phone, l.IsDefault, l.IsActive, l.CreatedAt, l.UpdatedAt
        )).ToList();
    }
}
