using Microsoft.Extensions.Logging;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

/// <summary>
/// Deterministic stand-in for a storefront/marketplace connector, used when
/// <c>MOCK_INTEGRATIONS=true</c>.
///
/// <para>Constructed once per platform rather than as a singleton, so mock mode
/// exercises the real multi-channel shape: several connectors resolved through
/// <see cref="IECommerceServiceFactory"/> at the same time. A single mock bound
/// to one platform would have hidden exactly the defect the factory exists to
/// fix.</para>
///
/// <para>Output is derived from the platform and the poll window rather than
/// randomised, so a repeated poll returns the same external ids and the
/// idempotent-replay path is actually exercised instead of minting new orders
/// forever.</para>
/// </summary>
public class MockECommerceService(ECommercePlatform platform, ILogger<MockECommerceService> logger)
    : IECommerceService
{
    public ECommercePlatform Platform => platform;

    /// <summary>Marketplaces collect and remit tax; storefronts leave it with the seller.</summary>
    private bool IsMarketplace => platform is ECommercePlatform.Amazon or ECommercePlatform.Ebay
        or ECommercePlatform.Etsy or ECommercePlatform.Walmart;

    public Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
    {
        logger.LogInformation(
            "[MockECommerce:{Platform}] PollOrders from {StoreUrl} since {Since}", platform, storeUrl, since);

        // Stable per (platform, UTC day) so repeated polls in a day replay the
        // same orders and exercise idempotency.
        var slug = $"{platform}-{since:yyyyMMdd}".ToUpperInvariant();

        var orders = new List<ECommerceOrder>
        {
            new()
            {
                ExternalId = $"MOCK-{slug}-1",
                OrderNumber = $"#{slug}-1",
                BuyerId = $"buyer-{platform}-alpha".ToLowerInvariant(),
                CustomerName = "Alex Rivera",
                CustomerEmail = IsMarketplace
                    // Marketplaces hand back a rotating relay address, never the
                    // real mailbox — the mock reflects that so nothing downstream
                    // starts treating email as an identity key.
                    ? $"{Guid.Empty.ToString("N")[..8]}@relay.{platform.ToString().ToLowerInvariant()}.example"
                    : "alex.rivera@example.com",
                CustomerPhone = "555-0142",
                MarketingConsent = !IsMarketplace,
                Lines =
                [
                    new ECommerceOrderLine
                    {
                        ExternalSku = "SKU-001",
                        ExternalListingId = $"LST-{platform}-001".ToUpperInvariant(),
                        ProductName = "Mock Product",
                        Quantity = 2m,
                        UnitPrice = 49.99m,
                        LineTotal = 99.98m,
                        TaxAmount = 8.25m,
                    },
                ],
                ShippingAddress = new ECommerceAddress
                {
                    Name = "Alex Rivera",
                    Line1 = "123 Main St",
                    City = "Springfield",
                    State = "IL",
                    PostalCode = "62701",
                    Country = "US",
                },
                SubtotalAmount = 99.98m,
                ShippingAmount = 6.50m,
                DiscountAmount = 0m,
                TaxAmount = 8.25m,
                TotalAmount = 114.73m,
                TaxCollectedBy = IsMarketplace ? TaxCollectedBy.Marketplace : TaxCollectedBy.Seller,
                PlatformFeeAmount = IsMarketplace ? 12.35m : null,
                CurrencyCode = "USD",
                OrderDate = since,
                PlatformStatus = "paid",
            },
        };

        return Task.FromResult<IReadOnlyList<ECommerceOrder>>(orders);
    }

    public Task<IReadOnlyList<ECommerceListing>> PollListingsAsync(
        string credentials, string storeUrl, CancellationToken ct)
    {
        logger.LogInformation("[MockECommerce:{Platform}] PollListings from {StoreUrl}", platform, storeUrl);

        IReadOnlyList<ECommerceListing> listings =
        [
            new()
            {
                ExternalListingId = $"LST-{platform}-001".ToUpperInvariant(),
                ExternalSku = "SKU-001",
                Title = "Mock Product",
                Price = 49.99m,
                AvailableQuantity = 25m,
            },
            // Deliberately unmapped-looking second listing so the triage surface
            // has something to show in a mock install.
            new()
            {
                ExternalListingId = $"LST-{platform}-002".ToUpperInvariant(),
                ExternalSku = "SKU-UNMAPPED",
                Title = "Mock Product (unmapped)",
                Price = 12.00m,
                AvailableQuantity = 4m,
            },
        ];

        return Task.FromResult(listings);
    }

    public Task<IReadOnlyList<ECommerceSettlement>> PollSettlementsAsync(
        string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
    {
        logger.LogInformation(
            "[MockECommerce:{Platform}] PollSettlements from {StoreUrl} since {Since}", platform, storeUrl, since);

        if (!IsMarketplace)
        {
            // Storefronts settle through your own processor, not through the
            // platform, so there is no payout batch to import.
            return Task.FromResult<IReadOnlyList<ECommerceSettlement>>([]);
        }

        var slug = $"{platform}-{since:yyyyMMdd}".ToUpperInvariant();

        IReadOnlyList<ECommerceSettlement> settlements =
        [
            new()
            {
                ExternalSettlementId = $"STL-{slug}",
                PeriodStart = since,
                PeriodEnd = since.AddDays(14),
                DepositedAt = since.AddDays(16),
                // 114.73 gross - 12.35 referral - 8.25 pass-through tax = 94.13
                NetAmount = 94.13m,
                CurrencyCode = "USD",
                Lines =
                [
                    new ECommerceSettlementLine
                    {
                        LineType = ChannelSettlementLineType.OrderProceeds,
                        ExternalOrderId = $"MOCK-{slug}-1",
                        Amount = 114.73m,
                        Description = "Order proceeds",
                    },
                    new ECommerceSettlementLine
                    {
                        LineType = ChannelSettlementLineType.ReferralFee,
                        ExternalOrderId = $"MOCK-{slug}-1",
                        Amount = -12.35m,
                        Description = "Referral fee",
                    },
                    new ECommerceSettlementLine
                    {
                        LineType = ChannelSettlementLineType.TaxCollected,
                        ExternalOrderId = $"MOCK-{slug}-1",
                        Amount = -8.25m,
                        Description = "Sales tax withheld and remitted by marketplace",
                    },
                ],
            },
        ];

        return Task.FromResult(settlements);
    }

    public Task SyncInventoryAsync(
        string credentials, string storeUrl, string externalListingId, decimal quantity, CancellationToken ct)
    {
        logger.LogInformation(
            "[MockECommerce:{Platform}] SyncInventory listing {Listing}: quantity={Quantity} to {StoreUrl}",
            platform, externalListingId, quantity, storeUrl);
        return Task.CompletedTask;
    }

    public Task UpdateOrderStatusAsync(
        string credentials, string storeUrl, string externalOrderId, string status,
        string? trackingNumber, CancellationToken ct)
    {
        logger.LogInformation(
            "[MockECommerce:{Platform}] UpdateOrderStatus {OrderId} to {Status} (tracking {Tracking}) on {StoreUrl}",
            platform, externalOrderId, status, trackingNumber ?? "none", storeUrl);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct)
    {
        logger.LogInformation("[MockECommerce:{Platform}] TestConnection to {StoreUrl}", platform, storeUrl);
        return Task.FromResult(true);
    }
}
