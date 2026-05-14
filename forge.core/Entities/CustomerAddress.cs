
namespace Forge.Core.Entities;

public class CustomerAddress : BaseAuditableEntity
{
    public int CustomerId { get; set; }
    public string Label { get; set; } = string.Empty;
    public AddressType AddressType { get; set; } = AddressType.Both;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";
    public bool IsDefault { get; set; }

    public Customer Customer { get; set; } = null!;
}
