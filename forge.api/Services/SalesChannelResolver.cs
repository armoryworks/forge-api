using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <inheritdoc cref="ISalesChannelResolver"/>
public class SalesChannelResolver(AppDbContext db) : ISalesChannelResolver
{
    public async Task<SalesChannel> ResolveAsync(int? channelId, CancellationToken ct)
    {
        if (channelId.HasValue)
        {
            return await db.SalesChannels.FirstOrDefaultAsync(c => c.Id == channelId.Value, ct)
                ?? throw new KeyNotFoundException($"SalesChannel {channelId.Value} not found");
        }

        return await db.SalesChannels.FirstOrDefaultAsync(c => c.IsDefault, ct)
            ?? throw new KeyNotFoundException(
                "No default sales channel is configured. One channel must carry IsDefault so orders " +
                "created without an explicit channel can be routed.");
    }

    public async Task<int> ResolveSoldToCustomerIdAsync(
        SalesChannel channel, int? requestedCustomerId, CancellationToken ct)
    {
        if (!channel.IsRetail)
        {
            return requestedCustomerId
                ?? throw new InvalidOperationException(
                    $"Channel '{channel.Code}' is account business — an order on it requires a customer.");
        }

        var houseAccountId = channel.SoldToCustomerId
            ?? throw new InvalidOperationException(
                $"Channel '{channel.Code}' is a retail channel but has no sold-to house account " +
                "configured. Set one before importing or entering orders on it — the receivable has " +
                "to land somewhere, and it is never the consumer.");

        // Verify the house account still exists and is usable. A soft-deleted or
        // deactivated house account would otherwise surface as an FK violation
        // deep inside order creation, long after the useful error message.
        var houseAccount = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == houseAccountId, ct)
            ?? throw new InvalidOperationException(
                $"Channel '{channel.Code}' points at sold-to customer {houseAccountId}, which no longer exists.");

        if (!houseAccount.IsActive)
        {
            throw new InvalidOperationException(
                $"Channel '{channel.Code}' bills to '{houseAccount.GetDisplayName()}', which is deactivated. " +
                "Reactivate it or point the channel at a different house account.");
        }

        return houseAccountId;
    }
}
