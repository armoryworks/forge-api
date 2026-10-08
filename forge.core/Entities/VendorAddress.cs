using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class VendorAddress : BaseAuditableEntity
{
    public int VendorId { get; set; }
    public string Label { get; set; } = string.Empty;
    public VendorAddressType AddressType { get; set; } = VendorAddressType.RemitTo;
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "US";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public Vendor Vendor { get; set; } = null!;
}
