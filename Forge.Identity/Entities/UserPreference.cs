namespace Forge.Identity.Entities;

public class UserPreference : BaseAuditableEntity
{
    public int UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
}
