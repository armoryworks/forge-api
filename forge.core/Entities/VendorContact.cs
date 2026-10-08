namespace Forge.Core.Entities;

public class VendorContact : BaseAuditableEntity
{
    public int VendorId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Fax { get; set; }
    public string? Role { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Vendor Vendor { get; set; } = null!;
}
