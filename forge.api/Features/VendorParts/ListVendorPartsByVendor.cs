using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Catalog of parts a given Vendor sources. Powers the
/// vendor-detail "Catalog" tab. Sorted by Part.PartNumber ASC for stable
/// alphabetic browsing.
/// </summary>
public record ListVendorPartsByVendorQuery(int VendorId)
    : IRequest<List<VendorPartResponseModel>>;

public class ListVendorPartsByVendorHandler(AppDbContext db)
    : IRequestHandler<ListVendorPartsByVendorQuery, List<VendorPartResponseModel>>
{
    public async Task<List<VendorPartResponseModel>> Handle(ListVendorPartsByVendorQuery request, CancellationToken ct)
    {
        var rows = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.Part)
            .Include(x => x.PriceTiers)
            .Where(x => x.VendorId == request.VendorId)
            .OrderBy(x => x.Part!.PartNumber)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(VendorPartMapper.ToResponse).ToList();
    }
}
