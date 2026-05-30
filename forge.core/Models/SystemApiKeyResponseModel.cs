namespace Forge.Core.Models;

public record SystemApiKeyResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public int UserId { get; init; }
    public string? UserEmail { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<string>? Scopes { get; init; }
    public List<string>? AllowedIps { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional role-template binding. Null when the key inherits the bound
    /// user's full role set; non-null when the auth handler narrows roles
    /// to the intersection of (user) ∩ (template).
    /// </summary>
    public int? RoleTemplateId { get; init; }

    /// <summary>Denormalized for the admin UI list. Null when no template is bound.</summary>
    public string? RoleTemplateName { get; init; }
}
