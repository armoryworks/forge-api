using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// One issued refresh token in a device's rotation family. Only the hash is
/// stored. Rotation marks the presented row consumed and inserts the
/// successor in the same family; presenting an already-consumed token is
/// treated as theft and revokes the whole family. Expired rows are pruned —
/// this ledger is exempt from soft delete by design.
/// </summary>
public class DeviceRefreshToken : BaseEntity
{
    public int UserDeviceId { get; set; }

    public int UserId { get; set; }

    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 of the raw token, lowercase hex.</summary>
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public UserDevice UserDevice { get; set; } = null!;
}
