namespace Forge.Core.Models;

/// <summary>
/// Issuance request for a user-bound system API key. The bound user gets the
/// key's permissions via their existing role grants — there is no role
/// override field. Scopes are reserved for future per-action narrowing.
/// </summary>
public record CreateSystemApiKeyRequestModel
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// FK to ApplicationUser. The key authenticates AS this user — request
    /// principals carry the user's id and roles.
    /// </summary>
    public int UserId { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Optional finer-grained scope grants beyond the user's roles. Reserved
    /// for future use; today the bound user's roles are the sole permission
    /// model. Persisted as <c>jsonb</c>.
    /// </summary>
    public List<string>? Scopes { get; init; }

    public List<string>? AllowedIps { get; init; }

    /// <summary>
    /// Optional <c>RoleTemplate</c> id that narrows the bound user's role
    /// set at auth time to the intersection of (user's roles) ∩ (template's
    /// IncludedRoleNames). When null (the default), the key inherits the
    /// bound user's full role set. The template can only narrow, never
    /// expand — see <c>SystemApiKey.RoleTemplateId</c>.
    /// </summary>
    public int? RoleTemplateId { get; init; }
}
