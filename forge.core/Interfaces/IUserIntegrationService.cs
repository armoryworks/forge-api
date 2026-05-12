using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public record UserIntegrationSummary(
    int Id,
    string Category,
    string ProviderId,
    string? DisplayName,
    bool IsActive,
    DateTimeOffset? LastSyncAt,
    string? LastError,
    DateTimeOffset CreatedAt);

public record IntegrationProviderInfo(
    string ProviderId,
    string Category,
    string DisplayName,
    string AuthType,
    string? Description,
    string? Icon);

public interface IUserIntegrationService
{
    Task<List<UserIntegrationSummary>> GetUserIntegrationsAsync(int userId, CancellationToken ct = default);
    Task<UserIntegration?> GetByIdAsync(int userId, int integrationId, CancellationToken ct = default);
    Task<UserIntegration> CreateAsync(int userId, string category, string providerId, string? displayName, string credentialsJson, string? configJson = null, CancellationToken ct = default);
    Task UpdateCredentialsAsync(int userId, int integrationId, string credentialsJson, CancellationToken ct = default);
    Task UpdateConfigAsync(int userId, int integrationId, string? configJson, CancellationToken ct = default);
    Task DisconnectAsync(int userId, int integrationId, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default);
    Task UpdateSyncStatusAsync(int integrationId, DateTimeOffset? lastSyncAt, string? lastError, CancellationToken ct = default);

    /// <summary>
    /// Admin-only: get integration summaries for a specific user (no credentials).
    /// </summary>
    Task<List<UserIntegrationSummary>> AdminGetUserIntegrationsAsync(int targetUserId, CancellationToken ct = default);

    /// <summary>
    /// Admin-only: revoke a user's integration with full audit trail.
    /// </summary>
    Task AdminRevokeAsync(int adminUserId, int targetUserId, int integrationId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Get available integration providers grouped by category.
    /// </summary>
    List<IntegrationProviderInfo> GetAvailableProviders();

    /// <summary>
    /// Decrypt and return credentials JSON for internal service use only.
    /// Never expose via API endpoints.
    /// </summary>
    Task<string?> GetDecryptedCredentialsAsync(int userId, int integrationId, CancellationToken ct = default);
}
