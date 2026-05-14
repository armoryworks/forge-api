
namespace Forge.Identity.Entities;

public class UserMfaDevice : BaseAuditableEntity
{
    public int UserId { get; set; }
    public MfaDeviceType DeviceType { get; set; }
    public string EncryptedSecret { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public bool IsVerified { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    // WebAuthn-specific
    public string? CredentialId { get; set; }
    public string? PublicKey { get; set; }
    public uint? SignCount { get; set; }

    // SMS/Email-specific
    public string? PhoneNumber { get; set; }
    public string? EmailAddress { get; set; }
}
