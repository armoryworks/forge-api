using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// An enrolled mobile device. Personal devices belong to one user; shared
/// devices (<see cref="IsShared"/>) are enrolled to the instance and identify
/// the person per transaction, kiosk-style. Revocation is server-side: the
/// next contact from a revoked device gets a device-revoked signal and the
/// app wipes this instance's local data.
/// </summary>
public class UserDevice : BaseAuditableEntity
{
    public int? UserId { get; set; }

    /// <summary>App-generated UUID, stored in the device's secure storage.</summary>
    [MaxLength(64)]
    public string DeviceUuid { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"ios" or "android".</summary>
    [MaxLength(20)]
    public string Platform { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? OsVersion { get; set; }

    [MaxLength(30)]
    public string? AppVersion { get; set; }

    public bool IsShared { get; set; }

    public int EnrolledByUserId { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public int? RevokedByUserId { get; set; }

    /// <summary>Set when a consumed refresh token is replayed — possible theft.</summary>
    public bool IsFlagged { get; set; }

    /// <summary>Shared devices only: SHA-256 of the device credential sent as X-Device-Token.</summary>
    [MaxLength(64)]
    public string? DeviceTokenHash { get; set; }
}
