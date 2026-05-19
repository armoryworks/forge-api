namespace Forge.Core.Models;

public record CreateSystemApiKeyResponseModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;

    /// <summary>
    /// The full plaintext key. Returned exactly once at creation. Never
    /// persisted, never returned from any other endpoint. Lose it = revoke
    /// and reissue.
    /// </summary>
    public string PlaintextKey { get; init; } = string.Empty;

    public int UserId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
