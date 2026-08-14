using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesChannels;

public record DeleteSalesChannelCommand(int Id) : IRequest;

public class DeleteSalesChannelHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DeleteSalesChannelCommand>
{
    public async Task Handle(DeleteSalesChannelCommand request, CancellationToken ct)
    {
        var channel = await db.SalesChannels.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"SalesChannel {request.Id} not found");

        if (channel.IsDefault)
        {
            throw new InvalidOperationException(
                "The default sales channel cannot be deleted. Make another channel the default first.");
        }

        // Orders keep a hard FK to their channel (Restrict, not SetNull) because
        // the channel is what explains why an order skipped credit and quoting,
        // and what its tax treatment was. Deactivate instead — the channel stops
        // accepting new orders but its history stays readable.
        var orderCount = await db.SalesOrders.CountAsync(o => o.ChannelId == request.Id, ct);
        if (orderCount > 0)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' has {orderCount} order(s) against it and cannot be deleted. " +
                "Deactivate it instead — existing orders keep their channel context.");
        }

        var buyerCount = await db.RetailBuyers.CountAsync(b => b.ChannelId == request.Id, ct);
        if (buyerCount > 0)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' has {buyerCount} retail buyer(s) against it and cannot be deleted. " +
                "Deactivate it instead.");
        }

        channel.DeletedAt = clock.UtcNow;

        db.LogActivityAt(
            "deleted",
            $"Deleted sales channel: {channel.Name} ({channel.Code})",
            ("SalesChannel", channel.Id));

        await db.SaveChangesAsync(ct);
    }
}
