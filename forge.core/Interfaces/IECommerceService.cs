using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// One storefront or marketplace connector. An implementation translates a
/// single platform's API into the normalised <see cref="ECommerceOrder"/> shape
/// and back.
///
/// <para><b>Connectors do not write domain state.</b> This interface returns
/// data; it never creates a <see cref="Entities.SalesOrder"/>. Order creation
/// belongs to the MediatR handler, which is where activity logging, capability
/// gating and validation live. The previous contract had an
/// <c>ImportOrderAsync</c> that returned a sales-order id, which put aggregate
/// construction in the integrations project and bypassed all three.</para>
///
/// <para>Resolve implementations through <see cref="IECommerceServiceFactory"/>,
/// never by injecting this directly — an install runs several platforms at once
/// and a bare injection can only bind one.</para>
/// </summary>
public interface IECommerceService
{
    ECommercePlatform Platform { get; }

    /// <summary>Orders created or updated on the platform since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct);

    /// <summary>Listings published on the platform, for mapping to parts and for inventory sync.</summary>
    Task<IReadOnlyList<ECommerceListing>> PollListingsAsync(
        string credentials, string storeUrl, CancellationToken ct);

    /// <summary>Payout batches the platform has settled since <paramref name="since"/>.</summary>
    Task<IReadOnlyList<ECommerceSettlement>> PollSettlementsAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct);

    /// <summary>Push an on-hand quantity to a listing.</summary>
    Task SyncInventoryAsync(
        string credentials, string storeUrl, string externalListingId, decimal quantity, CancellationToken ct);

    /// <summary>Report fulfilment back to the platform (shipped, tracking number, cancelled).</summary>
    Task UpdateOrderStatusAsync(
        string credentials, string storeUrl, string externalOrderId, string status,
        string? trackingNumber, CancellationToken ct);

    Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct);
}
