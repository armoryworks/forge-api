using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

public record RetryECommerceImportCommand(int SyncId) : IRequest<ECommerceOrderSyncResponseModel>;

public class RetryECommerceImportHandler(AppDbContext db, IECommerceService eCommerceService)
    : IRequestHandler<RetryECommerceImportCommand, ECommerceOrderSyncResponseModel>
{
    public async Task<ECommerceOrderSyncResponseModel> Handle(
        RetryECommerceImportCommand request, CancellationToken cancellationToken)
    {
        var sync = await db.ECommerceOrderSyncs
            .Include(s => s.Integration)
            .FirstOrDefaultAsync(s => s.Id == request.SyncId, cancellationToken)
            ?? throw new KeyNotFoundException($"ECommerceOrderSync {request.SyncId} not found");

        if (sync.Status != ECommerceOrderSyncStatus.Failed)
            throw new InvalidOperationException("Only failed imports can be retried");

        var order = System.Text.Json.JsonSerializer.Deserialize<ECommerceOrder>(sync.OrderDataJson)
            ?? throw new InvalidOperationException("Failed to deserialize stored order data");

        try
        {
            var salesOrderId = await eCommerceService.ImportOrderAsync(order, sync.IntegrationId, cancellationToken);
            sync.SalesOrderId = salesOrderId;
            sync.Status = ECommerceOrderSyncStatus.Imported;
            sync.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            sync.ErrorMessage = ex.Message;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ECommerceOrderSyncResponseModel
        {
            Id = sync.Id,
            ExternalOrderId = sync.ExternalOrderId,
            ExternalOrderNumber = sync.ExternalOrderNumber,
            SalesOrderId = sync.SalesOrderId,
            Status = sync.Status,
            ErrorMessage = sync.ErrorMessage,
            ImportedAt = sync.ImportedAt,
        };
    }
}
