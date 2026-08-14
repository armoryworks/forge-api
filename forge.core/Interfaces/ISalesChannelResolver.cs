using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

/// <summary>
/// Resolves which <see cref="SalesChannel"/> an order belongs to, and which
/// <see cref="Customer"/> should carry its receivable.
///
/// <para>Every order-creating path goes through this rather than reading
/// <c>SalesOrder.ChannelId</c> directly, because a null channel is legal and
/// means "the default channel" — the convention that let the column land on
/// existing installs without a NOT NULL backfill. Scattering that fallback
/// across handlers is how it eventually gets forgotten in one of them.</para>
/// </summary>
public interface ISalesChannelResolver
{
    /// <summary>
    /// The channel for <paramref name="channelId"/>, or the install's default
    /// channel when it is null.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// The id names no channel, or it is null and no default channel exists.
    /// The latter is a seeding failure, not a user error — the essential
    /// seeder and forge-db's seed/0020 both guarantee one.
    /// </exception>
    Task<SalesChannel> ResolveAsync(int? channelId, CancellationToken ct);

    /// <summary>
    /// The customer that owes the money for an order on this channel.
    ///
    /// <para>On <see cref="Enums.SalesChannelType.DirectB2B"/> this is
    /// <paramref name="requestedCustomerId"/> — the account IS the buyer. On
    /// retail and marketplace channels it is the channel's house account, and
    /// <paramref name="requestedCustomerId"/> is ignored: a consumer never
    /// carries the receivable.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A retail channel has no house account configured. Failing loudly here
    /// beats silently booking a consumer's order against an arbitrary customer.
    /// </exception>
    Task<int> ResolveSoldToCustomerIdAsync(SalesChannel channel, int? requestedCustomerId, CancellationToken ct);
}
