using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Forge.Core.Entities;

/// <summary>
/// Stored outcome of a mutating request that carried an Idempotency-Key.
/// A replay with the same key (and the same request fingerprint) inside the
/// retention window returns the stored response instead of re-executing.
/// Scoped per caller so keys never collide across users or devices. Rows are
/// pruned after expiry — exempt from soft delete by design.
/// </summary>
public class IdempotencyKey : BaseEntity
{
    [MaxLength(120)]
    public string Scope { get; set; } = string.Empty;

    [MaxLength(80)]
    [Column("idempotency_key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of method + path + body: the same key with a different request is refused.</summary>
    [MaxLength(64)]
    public string RequestFingerprint { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    [MaxLength(120)]
    public string? ContentType { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
