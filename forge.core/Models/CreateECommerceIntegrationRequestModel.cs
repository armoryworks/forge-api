
namespace Forge.Core.Models;

public record CreateECommerceIntegrationRequestModel
{
    public string Name { get; init; } = string.Empty;
    public ECommercePlatform Platform { get; init; }
    public string Credentials { get; init; } = string.Empty;
    public string? StoreUrl { get; init; }
    public bool AutoImportOrders { get; init; } = true;
    public bool SyncInventory { get; init; } = true;
    public int? DefaultCustomerId { get; init; }
}
