namespace Forge.Core.Entities;

public class PriceList : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? CustomerId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }

    public Customer? Customer { get; set; }
    public ICollection<PriceListEntry> Entries { get; set; } = [];
}
