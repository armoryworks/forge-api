namespace Forge.Identity.Entities;

public class UserScanIdentifier : BaseAuditableEntity
{
    public int UserId { get; set; }
    public string IdentifierType { get; set; } = string.Empty;
    public string IdentifierValue { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
