namespace Forge.Core.Entities;

public class BiApiKey : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? AllowedEntitySetsJson { get; set; }
    public string? AllowedIpsJson { get; set; }
}
