using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateECommerceIntegrationRequestModel
{
    public string Name { get; init; } = string.Empty;
    public ECommercePlatform Platform { get; init; }
    public string? Credentials { get; init; }
    public string? StoreUrl { get; init; }
    public bool IsActive { get; init; } = true;
    public bool AutoImportOrders { get; init; } = true;
    public bool SyncInventory { get; init; } = true;
    public int? DefaultCustomerId { get; init; }
}
