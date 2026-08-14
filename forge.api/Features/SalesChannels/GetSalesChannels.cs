using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesChannels;

/// <summary>
/// Channel list. Deliberately unpaged: channels are a handful of configured
/// routes to market, not transactional volume — an install with more than a
/// dozen has a different problem than pagination.
/// </summary>
public record GetSalesChannelsQuery(bool IncludeInactive = false, SalesChannelType? ChannelType = null)
    : IRequest<List<SalesChannelResponseModel>>;

public class GetSalesChannelsHandler(AppDbContext db)
    : IRequestHandler<GetSalesChannelsQuery, List<SalesChannelResponseModel>>
{
    public async Task<List<SalesChannelResponseModel>> Handle(
        GetSalesChannelsQuery request, CancellationToken ct)
    {
        var query = db.SalesChannels.AsNoTracking();

        if (!request.IncludeInactive)
            query = query.Where(c => c.IsActive);

        if (request.ChannelType.HasValue)
            query = query.Where(c => c.ChannelType == request.ChannelType.Value);

        return await query
            // Default first so the list reads as "here is where orders go by
            // default, and here is everything else".
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .Select(c => new SalesChannelResponseModel
            {
                Id = c.Id,
                Name = c.Name,
                Code = c.Code,
                Description = c.Description,
                ChannelType = c.ChannelType,
                SoldToCustomerId = c.SoldToCustomerId,
                SoldToCustomerName = c.SoldToCustomer == null
                    ? null
                    : (c.SoldToCustomer.CompanyName ?? c.SoldToCustomer.Name),
                TaxCollectedBy = c.TaxCollectedBy,
                IsDefault = c.IsDefault,
                IsActive = c.IsActive,
                OrderNumberPrefix = c.OrderNumberPrefix,
                ECommerceIntegrationId = c.ECommerceIntegrationId,
                IsRetail = c.ChannelType == SalesChannelType.DirectRetail
                    || c.ChannelType == SalesChannelType.Marketplace,
                OrderCount = c.SalesOrders.Count,
                ListingCount = c.Listings.Count,
                CreatedAt = c.CreatedAt,
            })
            .ToListAsync(ct);
    }
}
