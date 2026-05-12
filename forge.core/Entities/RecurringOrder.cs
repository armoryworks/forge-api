namespace Forge.Core.Entities;

public class RecurringOrder : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int? ShippingAddressId { get; set; }
    public int IntervalDays { get; set; }
    public DateTimeOffset NextGenerationDate { get; set; }
    public DateTimeOffset? LastGeneratedDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Customer Customer { get; set; } = null!;
    public CustomerAddress? ShippingAddress { get; set; }
    public ICollection<RecurringOrderLine> Lines { get; set; } = [];
}
