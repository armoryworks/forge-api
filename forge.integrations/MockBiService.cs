using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using Forge.Core.Interfaces;

namespace Forge.Integrations;

public class MockBiService : IBiService
{
    private readonly ILogger<MockBiService> _logger;

    public MockBiService(ILogger<MockBiService> logger)
    {
        _logger = logger;
    }

    public Task<string> GenerateApiKeyAsync(string name, DateTimeOffset? expiresAt, IReadOnlyList<string>? allowedEntitySets, CancellationToken ct)
    {
        _logger.LogInformation("[MockBi] GenerateApiKey for {Name}", name);
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var key = $"qbe_{Convert.ToBase64String(keyBytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
        return Task.FromResult(key);
    }

    public Task<bool> ValidateApiKeyAsync(string apiKey, CancellationToken ct)
    {
        _logger.LogInformation("[MockBi] ValidateApiKey {Prefix}...", apiKey[..Math.Min(12, apiKey.Length)]);
        return Task.FromResult(apiKey.StartsWith("qbe_"));
    }

    public Task RevokeApiKeyAsync(int keyId, CancellationToken ct)
    {
        _logger.LogInformation("[MockBi] RevokeApiKey {KeyId}", keyId);
        return Task.CompletedTask;
    }
}
