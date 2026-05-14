using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record ImportECommerceOrdersCommand(int IntegrationId) : IRequest<List<ECommerceOrderSyncResponseModel>>;

public class ImportECommerceOrdersHandler(AppDbContext db, IECommerceService eCommerceService, IClock clock)
    : IRequestHandler<ImportECommerceOrdersCommand, List<ECommerceOrderSyncResponseModel>>
{
    public async Task<List<ECommerceOrderSyncResponseModel>> Handle(
        ImportECommerceOrdersCommand request, CancellationToken cancellationToken)
    {
        var integration = await db.ECommerceIntegrations
            .FirstOrDefaultAsync(i => i.Id == request.IntegrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"ECommerceIntegration {request.IntegrationId} not found");

        var since = integration.LastSyncAt ?? DateTimeOffset.UtcNow.AddDays(-30);

        var orders = await eCommerceService.PollOrdersAsync(
            integration.EncryptedCredentials, integration.StoreUrl ?? string.Empty, since, cancellationToken);

        var results = new List<ECommerceOrderSyncResponseModel>();

        foreach (var order in orders)
        {
            var existingSync = await db.ECommerceOrderSyncs
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IntegrationId == request.IntegrationId
                    && s.ExternalOrderId == order.ExternalId, cancellationToken);

            if (existingSync != null)
            {
                results.Add(new ECommerceOrderSyncResponseModel
                {
                    Id = existingSync.Id,
                    ExternalOrderId = existingSync.ExternalOrderId,
                    ExternalOrderNumber = existingSync.ExternalOrderNumber,
                    SalesOrderId = existingSync.SalesOrderId,
                    Status = ECommerceOrderSyncStatus.Skipped,
                    ImportedAt = existingSync.ImportedAt,
                });
                continue;
            }

            var sync = new ECommerceOrderSync
            {
                IntegrationId = request.IntegrationId,
                ExternalOrderId = order.ExternalId,
                ExternalOrderNumber = order.OrderNumber,
                OrderDataJson = System.Text.Json.JsonSerializer.Serialize(order),
                ImportedAt = clock.UtcNow,
                Status = ECommerceOrderSyncStatus.Pending,
            };

            try
            {
                var salesOrderId = await eCommerceService.ImportOrderAsync(order, request.IntegrationId, cancellationToken);
                sync.SalesOrderId = salesOrderId;
                sync.Status = ECommerceOrderSyncStatus.Imported;
            }
            catch (Exception ex)
            {
                sync.Status = ECommerceOrderSyncStatus.Failed;
                sync.ErrorMessage = ex.Message;
            }

            db.ECommerceOrderSyncs.Add(sync);
            await db.SaveChangesAsync(cancellationToken);

            results.Add(new ECommerceOrderSyncResponseModel
            {
                Id = sync.Id,
                ExternalOrderId = sync.ExternalOrderId,
                ExternalOrderNumber = sync.ExternalOrderNumber,
                SalesOrderId = sync.SalesOrderId,
                Status = sync.Status,
                ErrorMessage = sync.ErrorMessage,
                ImportedAt = sync.ImportedAt,
            });
        }

        integration.LastSyncAt = clock.UtcNow;
        integration.LastError = null;
        await db.SaveChangesAsync(cancellationToken);

        return results;
    }
}
