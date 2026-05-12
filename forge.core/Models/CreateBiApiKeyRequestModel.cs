namespace Forge.Core.Models;

public record CreateBiApiKeyRequestModel
{
    public string Name { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<string>? AllowedEntitySets { get; init; }
    public List<string>? AllowedIps { get; init; }
}
