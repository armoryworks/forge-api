using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.VendorParts;

/// <summary>
/// Returns ALL <see cref="Core.Entities.VendorPartPriceTier"/> rows for a
/// VendorPart — current + closed — ordered by EffectiveFrom DESC, then
/// MinQuantity ASC. Powers the read-only price-tier history dialog on the
/// Vendor catalog.
///
/// Distinct from <see cref="GetVendorPartQuery"/>, which returns the
/// VendorPart with only its current effective tiers loaded into the
/// embedded PriceTiers collection.
/// </summary>
public record GetVendorPartPriceTierHistoryQuery(int VendorPartId) : IRequest<List<VendorPartPriceTierResponseModel>>;

public class GetVendorPartPriceTierHistoryHandler(AppDbContext db)
    : IRequestHandler<GetVendorPartPriceTierHistoryQuery, List<VendorPartPriceTierResponseModel>>
{
    public async Task<List<VendorPartPriceTierResponseModel>> Handle(
        GetVendorPartPriceTierHistoryQuery request, CancellationToken ct)
    {
        var exists = await db.VendorParts.AnyAsync(v => v.Id == request.VendorPartId, ct);
        if (!exists)
            throw new KeyNotFoundException($"VendorPart {request.VendorPartId} not found");

        var tiers = await db.VendorPartPriceTiers
            .AsNoTracking()
            .Where(t => t.VendorPartId == request.VendorPartId)
            .OrderByDescending(t => t.EffectiveFrom)
            .ThenBy(t => t.MinQuantity)
            .ToListAsync(ct);

        return tiers.Select(VendorPartMapper.ToTierResponse).ToList();
    }
}
