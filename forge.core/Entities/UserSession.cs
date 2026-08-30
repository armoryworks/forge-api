using System.ComponentModel.DataAnnotations;

namespace Forge.Core.Entities;

/// <summary>
/// A live access-token session, one row per JTI. Persisting sessions (rather
/// than the retired in-memory dictionary) keeps logins across API restarts
/// and makes revocation durable. Refresh rotates the JTI in place. Expired
/// rows are pruned — exempt from soft delete by design.
/// </summary>
public class UserSession : BaseEntity
{
    public int UserId { get; set; }

    [MaxLength(64)]
    public string Jti { get; set; } = string.Empty;

    /// <summary>Links mobile sessions to their device; null for web sessions.</summary>
    public int? UserDeviceId { get; set; }

    [MaxLength(30)]
    public string? AuthMethod { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastRefreshedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
