using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record GetECommerceIntegrationsQuery : IRequest<List<ECommerceIntegrationResponseModel>>;

public class GetECommerceIntegrationsHandler(AppDbContext db)
    : IRequestHandler<GetECommerceIntegrationsQuery, List<ECommerceIntegrationResponseModel>>
{
    public async Task<List<ECommerceIntegrationResponseModel>> Handle(
        GetECommerceIntegrationsQuery request, CancellationToken cancellationToken)
    {
        var integrations = await db.ECommerceIntegrations
            .AsNoTracking()
            .Include(i => i.DefaultCustomer)
            .OrderBy(i => i.Name)
            .ToListAsync(cancellationToken);

        var integrationIds = integrations.Select(i => i.Id).ToList();

        var syncCounts = await db.ECommerceOrderSyncs
            .AsNoTracking()
            .Where(s => integrationIds.Contains(s.IntegrationId))
            .GroupBy(s => s.IntegrationId)
            .Select(g => new { IntegrationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(s => s.IntegrationId, s => s.Count, cancellationToken);

        return integrations.Select(i => new ECommerceIntegrationResponseModel
        {
            Id = i.Id,
            Name = i.Name,
            Platform = i.Platform,
            StoreUrl = i.StoreUrl,
            IsActive = i.IsActive,
            AutoImportOrders = i.AutoImportOrders,
            SyncInventory = i.SyncInventory,
            LastSyncAt = i.LastSyncAt,
            LastError = i.LastError,
            DefaultCustomerId = i.DefaultCustomerId,
            DefaultCustomerName = i.DefaultCustomer?.CompanyName,
            OrderSyncCount = syncCounts.GetValueOrDefault(i.Id),
        }).ToList();
    }
}
