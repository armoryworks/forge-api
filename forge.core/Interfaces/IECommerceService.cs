using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IECommerceService
{
    ECommercePlatform Platform { get; }
    Task<IReadOnlyList<ECommerceOrder>> PollOrdersAsync(string credentials, string storeUrl, DateTimeOffset since, CancellationToken ct);
    Task<int> ImportOrderAsync(ECommerceOrder order, int integrationId, CancellationToken ct);
    Task SyncInventoryAsync(string credentials, string storeUrl, int partId, decimal quantity, CancellationToken ct);
    Task UpdateOrderStatusAsync(string credentials, string storeUrl, string externalOrderId, string status, CancellationToken ct);
    Task<bool> TestConnectionAsync(string credentials, string storeUrl, CancellationToken ct);
}
