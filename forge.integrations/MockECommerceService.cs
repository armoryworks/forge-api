using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockECommerceService : IECommerceService
{
    private readonly ILogger<MockECommerceService> _logger;

    public MockECommerceService(ILogger<MockECommerceService> logger)
    {
        _logger = logger;
    }

    public ECommercePlatform Platform => ECommercePlatform.Shopify;

    public Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct)
    {
        _logger.LogInformation("[MockECommerce] PollOrders from {StoreUrl} since {Since}", storeUrl, since);

        var orders = new List<ECommerceOrder>
        {
            new()
            {
                ExternalId = $"MOCK-{Guid.NewGuid().ToString("N")[..8]}",
                OrderNumber = $"#{Random.Shared.Next(1000, 9999)}",
                CustomerEmail = "customer@example.com",
                CustomerName = "Mock Customer",
                Lines =
                [
                    new ECommerceOrderLine
                    {
                        ExternalSku = "SKU-001",
                        ProductName = "Mock Product",
                        Quantity = 2,
                        UnitPrice = 49.99m,
                        LineTotal = 99.98m,
                    }
                ],
                ShippingAddress = new ECommerceAddress
                {
                    Name = "Mock Customer",
                    Line1 = "123 Main St",
                    City = "Springfield",
                    State = "IL",
                    PostalCode = "62701",
                    Country = "US",
                },
                TotalAmount = 99.98m,
                CurrencyCode = "USD",
                OrderDate = DateTimeOffset.UtcNow,
            }
        };

        return Task.FromResult<IReadOnlyList<ECommerceOrder>>(orders);
    }

    public Task<int> ImportOrderAsync(ECommerceOrder order, int integrationId, CancellationToken ct)
    {
        _logger.LogInformation("[MockECommerce] ImportOrder {OrderNumber} for integration {IntegrationId}",
            order.OrderNumber, integrationId);

        // Return a mock sales order ID
        return Task.FromResult(Random.Shared.Next(1000, 9999));
    }

    public Task SyncInventoryAsync(string credentials, string storeUrl, int partId, decimal quantity, CancellationToken ct)
    {
        _logger.LogInformation("[MockECommerce] SyncInventory for part {PartId}: quantity={Quantity} to {StoreUrl}",
            partId, quantity, storeUrl);
        return Task.CompletedTask;
    }

    public Task UpdateOrderStatusAsync(string credentials, string storeUrl, string externalOrderId, string status, CancellationToken ct)
    {
        _logger.LogInformation("[MockECommerce] UpdateOrderStatus {OrderId} to {Status} on {StoreUrl}",
            externalOrderId, status, storeUrl);
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct)
    {
        _logger.LogInformation("[MockECommerce] TestConnection to {StoreUrl}", storeUrl);
        return Task.FromResult(true);
    }
}
