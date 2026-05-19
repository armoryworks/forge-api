namespace Forge.Core.Entities;

/// <summary>
/// User-bound API key for headless system-to-system integrations.
///
/// Distinct from <see cref="BiApiKey"/>:
///   - <c>BiApiKey</c> is <b>unbound</b> — it carries a synthetic
///     "BiApiClient" role and a synthetic NameIdentifier (the key id). Used
///     for read-only BI exports where the audit trail doesn't need to
///     attribute back to a real person.
///   - <c>SystemApiKey</c> is <b>user-bound</b> — every key references an
///     <see cref="Forge.Data.Context.ApplicationUser"/> via <see cref="UserId"/>.
///     The authentication handler hydrates the principal AS that user, so
///     <c>AppDbContext.CurrentUserId</c>, audit logs, activity logs, and
///     <c>[Authorize(Roles = ...)]</c> checks all see the real user with
///     the user's actual role grants.
///
/// Intended for headless outbox-style sync clients that need to authenticate
/// as a narrowly-scoped service identity (typically created with a single-
/// purpose role such as <c>LeadIntake</c>). See
/// <c>docs/api-key-integrations.md</c> for the issuance and consumer
/// contract.
///
/// FK-only pattern (no <see cref="Forge.Data.Context.ApplicationUser"/> nav
/// property) — Core cannot reference Data.
/// </summary>
public class SystemApiKey : BaseAuditableEntity
{
    /// <summary>Human-readable label shown in admin UI. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash of the plaintext key (via <c>PasswordHasher</c>). The
    /// plaintext is returned to the caller exactly once at issuance and
    /// never persisted.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// First 12 characters of the plaintext, persisted at issuance to allow
    /// indexed prefix lookup at authentication time (PBKDF2 hashes are
    /// salted per-row so reverse lookup by hash is impossible).
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// FK to <see cref="Forge.Data.Context.ApplicationUser"/>. The key
    /// authenticates AS this user — the principal that the auth handler
    /// builds carries the user's id, email, and role grants.
    /// </summary>
    public int UserId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Optional finer-grained scope grants beyond the user's roles. JSON
    /// array of scope strings (e.g. <c>["leads:write","customers:read"]</c>).
    /// Reserved for future use; today the bound user's roles are the sole
    /// permission model. Stored as <c>jsonb</c>.
    /// </summary>
    public string? ScopesJson { get; set; }

    /// <summary>
    /// Optional IP allow-list. JSON array of IPv4/IPv6 strings. When set,
    /// requests from any other address are rejected post-key-verify (per
    /// the BiApiKey precedent — IP check after PBKDF2 to avoid leaking
    /// key existence). Stored as <c>jsonb</c>.
    /// </summary>
    public string? AllowedIpsJson { get; set; }
}
