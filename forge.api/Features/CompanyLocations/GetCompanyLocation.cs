using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.CompanyLocations;

public record GetCompanyLocationQuery(int Id) : IRequest<CompanyLocationResponseModel>;

public class GetCompanyLocationHandler(AppDbContext db)
    : IRequestHandler<GetCompanyLocationQuery, CompanyLocationResponseModel>
{
    public async Task<CompanyLocationResponseModel> Handle(
        GetCompanyLocationQuery request, CancellationToken ct)
    {
        var l = await db.CompanyLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Company location {request.Id} not found");

        return new CompanyLocationResponseModel(
            l.Id, l.Name, l.Line1, l.Line2, l.City, l.State, l.PostalCode,
            l.Country, l.Phone, l.IsDefault, l.IsActive, l.CreatedAt, l.UpdatedAt);
    }
}
