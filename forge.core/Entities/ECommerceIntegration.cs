using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class ECommerceIntegration : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public ECommercePlatform Platform { get; set; }
    public string EncryptedCredentials { get; set; } = string.Empty;
    public string? StoreUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoImportOrders { get; set; } = true;
    public bool SyncInventory { get; set; } = true;
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    public string? PartMappingsJson { get; set; }
    public int? DefaultCustomerId { get; set; }

    public Customer? DefaultCustomer { get; set; }
    public ICollection<ECommerceOrderSync> OrderSyncs { get; set; } = [];
}
