using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Pillar 3 — Single VendorPart read with PriceTiers + denormalized vendor
/// company name + part number/name. Tier filter mirrors the list query:
/// currently-effective by default, all rows when <c>ShowHistory = true</c>.
/// </summary>
public record GetVendorPartQuery(int Id, bool ShowHistory = false) : IRequest<VendorPartResponseModel>;

public class GetVendorPartHandler(AppDbContext db, IClock clock)
    : IRequestHandler<GetVendorPartQuery, VendorPartResponseModel>
{
    public async Task<VendorPartResponseModel> Handle(GetVendorPartQuery request, CancellationToken ct)
    {
        var vp = await db.VendorParts
            .Include(x => x.Vendor)
            .Include(x => x.Part)
            .Include(x => x.PriceTiers)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"VendorPart {request.Id} not found");

        if (!request.ShowHistory)
        {
            var now = clock.UtcNow;
            vp.PriceTiers = vp.PriceTiers
                .Where(t => t.EffectiveFrom <= now
                    && (t.EffectiveTo == null || t.EffectiveTo >= now))
                .ToList();
        }

        return VendorPartMapper.ToResponse(vp);
    }
}
