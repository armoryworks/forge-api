using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.RetailOrders;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.ECommerce;

/// <summary>
/// Re-attempts a single failed import from the payload captured at poll time,
/// so an operator who has fixed the cause (mapped the SKU, set the channel's
/// house account, reactivated a part) does not have to wait for the next poll
/// window — or worse, widen it and risk re-importing everything.
/// </summary>
public record RetryECommerceImportCommand(int SyncId) : IRequest<ECommerceOrderSyncResponseModel>;

public class RetryECommerceImportHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<RetryECommerceImportCommand, ECommerceOrderSyncResponseModel>
{
    public async Task<ECommerceOrderSyncResponseModel> Handle(
        RetryECommerceImportCommand request, CancellationToken ct)
    {
        var sync = await db.ECommerceOrderSyncs
            .Include(s => s.Integration)
            .FirstOrDefaultAsync(s => s.Id == request.SyncId, ct)
            ?? throw new KeyNotFoundException($"ECommerceOrderSync {request.SyncId} not found");

        if (sync.Status != ECommerceOrderSyncStatus.Failed)
            throw new InvalidOperationException("Only failed imports can be retried");

        var order = JsonSerializer.Deserialize<ECommerceOrder>(sync.OrderDataJson)
            ?? throw new InvalidOperationException("Failed to deserialize stored order data");

        // The channel is reached through the integration rather than stored on
        // the sync row, so a channel re-pointed at a different house account
        // takes effect on retry.
        var channel = await db.SalesChannels
            .FirstOrDefaultAsync(c => c.ECommerceIntegrationId == sync.IntegrationId, ct)
            ?? throw new InvalidOperationException(
                $"No sales channel is attached to integration {sync.IntegrationId}; cannot retry the import. " +
                "Attach the integration to a retail channel first.");

        try
        {
            var result = await mediator.Send(
                new CreateRetailOrderCommand(ImportChannelOrdersHandler.ToRetailOrderModel(channel, order)), ct);

            sync.SalesOrderId = result.Order.Id;
            sync.Status = ECommerceOrderSyncStatus.Imported;
            sync.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            // Status stays Failed — only the message is refreshed, so the row
            // keeps showing why it is still stuck rather than looking untried.
            sync.ErrorMessage = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
        }

        await db.SaveChangesAsync(ct);

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
