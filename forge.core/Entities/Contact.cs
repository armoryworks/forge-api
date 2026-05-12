namespace Forge.Core.Entities;

public class Contact : BaseAuditableEntity
{
    public int CustomerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
    public bool IsPrimary { get; set; }

    public Customer Customer { get; set; } = null!;
}
