namespace Forge.Core.Entities;

public class MfaRecoveryCode : BaseAuditableEntity
{
    public int UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedFromIp { get; set; }
}
