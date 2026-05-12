using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

public class UserIntegrationService(
    AppDbContext db,
    ITokenEncryptionService encryption,
    IActivityLogRepository activityLog,
    ILogger<UserIntegrationService> logger) : IUserIntegrationService
{
    private static readonly List<IntegrationProviderInfo> Providers =
    [
        // Calendar
        new("google_calendar", "calendar", "Google Calendar", "oauth2", "Sync events with Google Calendar", "event"),
        new("outlook_calendar", "calendar", "Microsoft Outlook/365", "oauth2", "Sync events with Outlook Calendar", "event"),
        new("apple_calendar", "calendar", "Apple iCloud Calendar", "app_password", "Sync events with Apple Calendar", "event"),
        new("caldav", "calendar", "CalDAV (Generic)", "basic", "Connect to any CalDAV-compatible calendar", "event"),
        new("ics_feed", "calendar", "ICS Feed (Read-Only)", "none", "Subscribe to your QB Engineer calendar feed", "rss_feed"),

        // Messaging
        new("slack", "messaging", "Slack", "webhook", "Send notifications to Slack channels", "chat"),
        new("teams", "messaging", "Microsoft Teams", "webhook", "Send notifications to Teams channels", "chat"),
        new("discord", "messaging", "Discord", "webhook", "Send notifications to Discord channels", "chat"),
        new("google_chat", "messaging", "Google Chat", "webhook", "Send notifications to Google Chat spaces", "chat"),
        new("smtp", "messaging", "Email (Personal SMTP)", "basic", "Route notifications to a personal email", "email"),

        // Cloud Storage
        new("google_drive", "storage", "Google Drive", "oauth2", "Sync files to Google Drive", "cloud"),
        new("onedrive", "storage", "Microsoft OneDrive", "oauth2", "Sync files to OneDrive", "cloud"),
        new("dropbox", "storage", "Dropbox", "oauth2", "Sync files to Dropbox", "cloud"),
        new("icloud_drive", "storage", "Apple iCloud Drive", "app_password", "Sync files to iCloud Drive", "cloud"),
        new("s3", "storage", "S3-Compatible (MinIO, AWS, Wasabi)", "access_key", "Sync files to S3-compatible storage", "cloud"),

        // Other
        new("github", "other", "GitHub", "oauth2", "Log issues to a GitHub repository", "bug_report"),
    ];

    public async Task<List<UserIntegrationSummary>> GetUserIntegrationsAsync(int userId, CancellationToken ct = default)
    {
        return await db.UserIntegrations
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.Category)
            .ThenBy(i => i.ProviderId)
            .Select(i => new UserIntegrationSummary(
                i.Id, i.Category, i.ProviderId, i.DisplayName,
                i.IsActive, i.LastSyncAt, i.LastError, i.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<UserIntegration?> GetByIdAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        return await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct);
    }

    public async Task<UserIntegration> CreateAsync(
        int userId, string category, string providerId, string? displayName,
        string credentialsJson, string? configJson = null, CancellationToken ct = default)
    {
        var encrypted = encryption.Encrypt(credentialsJson);

        var integration = new UserIntegration
        {
            UserId = userId,
            Category = category,
            ProviderId = providerId,
            DisplayName = displayName,
            EncryptedCredentials = encrypted,
            ConfigJson = configJson,
            IsActive = true,
        };

        db.UserIntegrations.Add(integration);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} connected integration {ProviderId}", userId, providerId);
        return integration;
    }

    public async Task UpdateCredentialsAsync(int userId, int integrationId, string credentialsJson, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        integration.EncryptedCredentials = encryption.Encrypt(credentialsJson);
        integration.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateConfigAsync(int userId, int integrationId, string? configJson, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        integration.ConfigJson = configJson;
        await db.SaveChangesAsync(ct);
    }

    public async Task DisconnectAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        integration.IsActive = false;
        integration.EncryptedCredentials = string.Empty;
        integration.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} disconnected integration {ProviderId}", userId, integration.ProviderId);
    }

    public async Task<bool> TestConnectionAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        // For now, verify credentials can be decrypted
        try
        {
            var creds = encryption.Decrypt(integration.EncryptedCredentials);
            if (string.IsNullOrWhiteSpace(creds))
                return false;

            integration.LastError = null;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            integration.LastError = ex.Message;
            await db.SaveChangesAsync(ct);
            return false;
        }
    }

    public async Task UpdateSyncStatusAsync(int integrationId, DateTimeOffset? lastSyncAt, string? lastError, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations.FindAsync([integrationId], ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found");

        integration.LastSyncAt = lastSyncAt;
        integration.LastError = lastError;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<UserIntegrationSummary>> AdminGetUserIntegrationsAsync(int targetUserId, CancellationToken ct = default)
    {
        // Admin view — never includes credentials
        return await db.UserIntegrations
            .Where(i => i.UserId == targetUserId)
            .OrderBy(i => i.Category)
            .Select(i => new UserIntegrationSummary(
                i.Id, i.Category, i.ProviderId, i.DisplayName,
                i.IsActive, i.LastSyncAt, i.LastError, i.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task AdminRevokeAsync(int adminUserId, int targetUserId, int integrationId, string? reason, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == targetUserId, ct)
            ?? throw new KeyNotFoundException($"Integration {integrationId} not found for user {targetUserId}");

        var providerId = integration.ProviderId;

        integration.IsActive = false;
        integration.EncryptedCredentials = string.Empty;
        integration.DeletedAt = DateTimeOffset.UtcNow;
        integration.DeletedBy = adminUserId.ToString();
        await db.SaveChangesAsync(ct);

        // Audit trail
        await activityLog.AddAsync(new ActivityLog
        {
            EntityType = "UserIntegration",
            EntityId = integrationId,
            UserId = adminUserId,
            Action = "Revoked",
            Description = $"Admin revoked {providerId} integration for user {targetUserId}. Reason: {reason ?? "Not specified"}",
        }, ct);
        await activityLog.SaveChangesAsync(ct);

        logger.LogWarning(
            "Admin {AdminUserId} revoked integration {IntegrationId} ({ProviderId}) for user {TargetUserId}. Reason: {Reason}",
            adminUserId, integrationId, providerId, targetUserId, reason ?? "Not specified");
    }

    public List<IntegrationProviderInfo> GetAvailableProviders() => Providers;

    public async Task<string?> GetDecryptedCredentialsAsync(int userId, int integrationId, CancellationToken ct = default)
    {
        var integration = await db.UserIntegrations
            .FirstOrDefaultAsync(i => i.Id == integrationId && i.UserId == userId, ct);

        if (integration is null || string.IsNullOrWhiteSpace(integration.EncryptedCredentials))
            return null;

        return encryption.Decrypt(integration.EncryptedCredentials);
    }
}
