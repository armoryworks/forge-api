namespace Forge.Core.Models;

public record BiApiKeyResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<string>? AllowedEntitySets { get; init; }
    public List<string>? AllowedIps { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
