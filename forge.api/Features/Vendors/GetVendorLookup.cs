using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Vendors;

public record GetVendorLookupQuery(string? Term, int Limit) : IRequest<List<VendorLookupModel>>;

public class GetVendorLookupHandler(AppDbContext db)
    : IRequestHandler<GetVendorLookupQuery, List<VendorLookupModel>>
{
    public const int MaxLimit = 50;

    public async Task<List<VendorLookupModel>> Handle(GetVendorLookupQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var query = db.Vendors.AsNoTracking().Where(v => v.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Term))
        {
            var term = request.Term.Trim().ToLower();
            query = query.Where(v =>
                v.CompanyName.ToLower().Contains(term) ||
                (v.VendorNumber != null && v.VendorNumber.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(v => v.CompanyName)
            .Take(limit)
            .Select(v => new VendorLookupModel(v.Id, v.CompanyName, v.VendorNumber))
            .ToListAsync(cancellationToken);
    }
}
