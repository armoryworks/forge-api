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
    /// <summary>
    /// Superseded by <see cref="SalesChannel.SoldToCustomerId"/>. This was the
    /// original "which customer do imported orders bill to" hook, but it was
    /// only ever stored and displayed — no import path read it. The house
    /// account now lives on the channel, which is the right level: an
    /// integration supplies credentials, while the channel is what decides
    /// where the receivable lands and who owes the tax.
    ///
    /// <para>Kept on the row so existing installs do not lose the value, and
    /// deliberately not surfaced in the admin UI. Drop the column once no
    /// install has a non-null value worth migrating.</para>
    /// </summary>
    public int? DefaultCustomerId { get; set; }

    public Customer? DefaultCustomer { get; set; }
    public ICollection<ECommerceOrderSync> OrderSyncs { get; set; } = [];
}
