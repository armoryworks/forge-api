namespace Forge.Core.Models;

public record CreateBiApiKeyResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public string PlaintextKey { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}
