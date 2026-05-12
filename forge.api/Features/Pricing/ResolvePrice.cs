using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Pricing;

public record ResolvePriceQuery(int PartId, int? CustomerId, int Quantity = 1)
    : IRequest<PriceResolutionResponseModel>;

public class ResolvePriceHandler(AppDbContext db)
    : IRequestHandler<ResolvePriceQuery, PriceResolutionResponseModel>
{
    public async Task<PriceResolutionResponseModel> Handle(ResolvePriceQuery request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Try customer-specific price list
        if (request.CustomerId.HasValue)
        {
            var customerResult = await ResolvePriceFromListAsync(
                request.PartId, request.Quantity, request.CustomerId.Value, now, ct);

            if (customerResult is not null)
                return customerResult;
        }

        // 2. Try default price list
        var defaultResult = await ResolvePriceFromDefaultListAsync(
            request.PartId, request.Quantity, now, ct);

        if (defaultResult is not null)
            return defaultResult;

        // 3. No price found
        return new PriceResolutionResponseModel(null, null, "None");
    }

    private async Task<PriceResolutionResponseModel?> ResolvePriceFromListAsync(
        int partId, int quantity, int customerId, DateTimeOffset now, CancellationToken ct)
    {
        var entry = await db.PriceListEntries
            .Include(e => e.PriceList)
            .Where(e => e.PriceList.CustomerId == customerId
                && e.PriceList.IsActive
                && (e.PriceList.EffectiveFrom == null || e.PriceList.EffectiveFrom <= now)
                && (e.PriceList.EffectiveTo == null || e.PriceList.EffectiveTo >= now)
                && e.PartId == partId
                && e.MinQuantity <= quantity)
            .OrderByDescending(e => e.MinQuantity)
            .FirstOrDefaultAsync(ct);

        if (entry is null)
            return null;

        return new PriceResolutionResponseModel(entry.UnitPrice, entry.PriceList.Name, "Customer");
    }

    private async Task<PriceResolutionResponseModel?> ResolvePriceFromDefaultListAsync(
        int partId, int quantity, DateTimeOffset now, CancellationToken ct)
    {
        var entry = await db.PriceListEntries
            .Include(e => e.PriceList)
            .Where(e => e.PriceList.IsDefault
                && e.PriceList.IsActive
                && (e.PriceList.EffectiveFrom == null || e.PriceList.EffectiveFrom <= now)
                && (e.PriceList.EffectiveTo == null || e.PriceList.EffectiveTo >= now)
                && e.PartId == partId
                && e.MinQuantity <= quantity)
            .OrderByDescending(e => e.MinQuantity)
            .FirstOrDefaultAsync(ct);

        if (entry is null)
            return null;

        return new PriceResolutionResponseModel(entry.UnitPrice, entry.PriceList.Name, "Default");
    }
}
