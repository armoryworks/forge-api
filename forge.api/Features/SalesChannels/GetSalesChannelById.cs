using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.SalesChannels;

public record GetSalesChannelByIdQuery(int Id) : IRequest<SalesChannelResponseModel>;

public class GetSalesChannelByIdHandler(AppDbContext db)
    : IRequestHandler<GetSalesChannelByIdQuery, SalesChannelResponseModel>
{
    public Task<SalesChannelResponseModel> Handle(GetSalesChannelByIdQuery request, CancellationToken ct)
        => GetSalesChannelById.ProjectAsync(db, request.Id, ct);
}

/// <summary>
/// Shared projection so create / update / get-by-id return byte-identical
/// shapes. Counts are computed in the same query rather than in a Select
/// sub-Where, which would be an N+1 per the efficiency rules.
/// </summary>
public static class GetSalesChannelById
{
    public static async Task<SalesChannelResponseModel> ProjectAsync(
        AppDbContext db, int id, CancellationToken ct)
    {
        var result = await db.SalesChannels
            .AsNoTracking()
            .Where(c => c.Id == id)
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
                IsRetail = c.ChannelType == Core.Enums.SalesChannelType.DirectRetail
                    || c.ChannelType == Core.Enums.SalesChannelType.Marketplace,
                OrderCount = c.SalesOrders.Count,
                ListingCount = c.Listings.Count,
                CreatedAt = c.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return result ?? throw new KeyNotFoundException($"SalesChannel {id} not found");
    }
}
