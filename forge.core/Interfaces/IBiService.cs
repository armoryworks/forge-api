namespace Forge.Core.Interfaces;

public interface IBiService
{
    Task<string> GenerateApiKeyAsync(string name, DateTimeOffset? expiresAt, IReadOnlyList<string>? allowedEntitySets, CancellationToken ct);
    Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken ct);
    Task RevokeApiKeyAsync(int keyId, CancellationToken ct);
}
