using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Default <see cref="ICloudStorageTokenManager"/> implementation.
/// Decrypts the stored ciphertext via <see cref="ITokenEncryptionService"/>,
/// checks for near-expiry, refreshes via the provider's
/// <see cref="ICloudStorageIntegrationService.RefreshTokenAsync"/>, re-
/// encrypts the rotated tokens, and persists.
///
/// <para>Refresh buffer is 5 minutes — tokens that expire within that
/// window are refreshed proactively so an in-flight folder operation
/// doesn't 401 mid-call.</para>
///
/// <para>The mock provider's "tokens" are passed through verbatim
/// (no encryption / refresh). Tests + dev installs depend on this so
/// they don't need real OAuth plumbing.</para>
/// </summary>
public class CloudStorageTokenManager : ICloudStorageTokenManager
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ITokenEncryptionService _encryption;
    private readonly ICloudStorageResolver _providerResolver;
    private readonly ILogger<CloudStorageTokenManager> _logger;

    public CloudStorageTokenManager(
        AppDbContext db,
        ITokenEncryptionService encryption,
        ICloudStorageResolver providerResolver,
        ILogger<CloudStorageTokenManager> logger)
    {
        _db = db;
        _encryption = encryption;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    public async Task<string?> GetValidAccessTokenAsync(CloudStorageProvider provider, CancellationToken ct)
    {
        // Mock provider: tokens are placeholder strings; no encryption /
        // refresh needed. The mock service ignores the token value.
        if (string.Equals(provider.ProviderCode, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return "mock-token";
        }

        if (string.IsNullOrEmpty(provider.OAuthTokenEncrypted) ||
            string.IsNullOrEmpty(provider.RefreshTokenEncrypted))
        {
            _logger.LogWarning(
                "CloudStorageTokenManager: provider {Code} (id={Id}) has no usable tokens — admin must reconnect",
                provider.ProviderCode, provider.Id);
            return null;
        }

        var accessToken = TryDecrypt(provider.OAuthTokenEncrypted, $"provider:{provider.Id}:access");
        var refreshToken = TryDecrypt(provider.RefreshTokenEncrypted, $"provider:{provider.Id}:refresh");
        if (accessToken is null || refreshToken is null) return null;

        // If still valid, return as-is.
        if (provider.TokenExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            return accessToken;
        }

        // Refresh path.
        var service = _providerResolver.ResolveByCode(provider.ProviderCode);
        if (service is null)
        {
            _logger.LogWarning("CloudStorageTokenManager: provider code '{Code}' not registered — can't refresh",
                provider.ProviderCode);
            return null;
        }

        try
        {
            var refreshed = await service.RefreshTokenAsync(refreshToken, ct);
            provider.OAuthTokenEncrypted = _encryption.Encrypt(refreshed.AccessToken);
            provider.RefreshTokenEncrypted = _encryption.Encrypt(refreshed.RefreshToken);
            provider.TokenExpiresAt = refreshed.ExpiresAt;
            provider.LastConnectedAt = DateTimeOffset.UtcNow;
            provider.LastError = null;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("CloudStorageTokenManager: refreshed access token for provider {Code}",
                provider.ProviderCode);
            return refreshed.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloudStorageTokenManager: token refresh failed for provider {Code}",
                provider.ProviderCode);
            provider.LastError = $"Token refresh failed: {ex.Message}";
            await _db.SaveChangesAsync(ct);
            return null;
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(UserCloudStorageLink link, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(link.OAuthTokenEncrypted) ||
            string.IsNullOrEmpty(link.RefreshTokenEncrypted))
        {
            _logger.LogWarning(
                "CloudStorageTokenManager: UserCloudStorageLink (id={Id}) has no usable tokens",
                link.Id);
            return null;
        }

        var accessToken = TryDecrypt(link.OAuthTokenEncrypted, $"userLink:{link.Id}:access");
        var refreshToken = TryDecrypt(link.RefreshTokenEncrypted, $"userLink:{link.Id}:refresh");
        if (accessToken is null || refreshToken is null) return null;

        if (link.TokenExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            link.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return accessToken;
        }

        // Resolve the parent provider to pick the service for refresh.
        var provider = link.Provider ?? await _db.CloudStorageProviders.FindAsync([link.ProviderId], ct);
        if (provider is null)
        {
            _logger.LogWarning("CloudStorageTokenManager: parent CloudStorageProvider {Id} not found for user link {LinkId}",
                link.ProviderId, link.Id);
            return null;
        }

        var service = _providerResolver.ResolveByCode(provider.ProviderCode);
        if (service is null)
        {
            _logger.LogWarning("CloudStorageTokenManager: provider code '{Code}' not registered — can't refresh user link {LinkId}",
                provider.ProviderCode, link.Id);
            return null;
        }

        try
        {
            var refreshed = await service.RefreshTokenAsync(refreshToken, ct);
            link.OAuthTokenEncrypted = _encryption.Encrypt(refreshed.AccessToken);
            link.RefreshTokenEncrypted = _encryption.Encrypt(refreshed.RefreshToken);
            link.TokenExpiresAt = refreshed.ExpiresAt;
            link.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("CloudStorageTokenManager: refreshed access token for user link {LinkId}",
                link.Id);
            return refreshed.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloudStorageTokenManager: token refresh failed for user link {LinkId}", link.Id);
            return null;
        }
    }

    private string? TryDecrypt(string ciphertext, string contextLabel)
    {
        try
        {
            return _encryption.Decrypt(ciphertext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CloudStorageTokenManager: failed to decrypt token for {Context}", contextLabel);
            return null;
        }
    }
}
