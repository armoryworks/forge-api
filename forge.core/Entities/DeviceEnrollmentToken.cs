using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// A single-use device-enrollment token, minted by an admin and carried to
/// the phone in a QR code. Ten-minute lifetime, bound to the target user (or
/// to the instance for shared devices) and to the issuing admin. Only the
/// hash is stored. Expired rows are pruned — exempt from soft delete by
/// design.
/// </summary>
public class DeviceEnrollmentToken : BaseEntity
{
    /// <summary>SHA-256 of the raw token, lowercase hex.</summary>
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>The user the device will belong to; null for shared devices.</summary>
    public int? TargetUserId { get; set; }

    public bool IsShared { get; set; }

    public int IssuedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public int? ConsumedByDeviceId { get; set; }
}
