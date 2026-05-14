
namespace Forge.Core.Models;

public record ECommerceIntegrationResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ECommercePlatform Platform { get; init; }
    public string? StoreUrl { get; init; }
    public bool IsActive { get; init; }
    public bool AutoImportOrders { get; init; }
    public bool SyncInventory { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public string? LastError { get; init; }
    public int? DefaultCustomerId { get; init; }
    public string? DefaultCustomerName { get; init; }
    public int OrderSyncCount { get; init; }
}
