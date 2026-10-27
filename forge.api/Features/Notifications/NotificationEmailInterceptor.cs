using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Api.Capabilities;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Notifications;

public sealed class NotificationEmailInterceptor(
    ICapabilitySnapshotProvider capabilities,
    IClock clock,
    IConfiguration configuration,
    ILogger<NotificationEmailInterceptor> logger) : SaveChangesInterceptor
{
    public const string CapabilityCode = "CAP-CROSS-NOTIFICATIONS";
    public const string EmailEnabledSettingKey = "notifications.email_enabled";
    private const int MaxAttempts = 5;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db && TryGetCandidates(db, out var candidates))
        {
            try
            {
                EnqueueAsync(db, candidates, sync: true, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification email skipped for {Count} notification(s)", candidates.Count);
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db && TryGetCandidates(db, out var candidates))
        {
            try
            {
                await EnqueueAsync(db, candidates, sync: false, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification email skipped for {Count} notification(s)", candidates.Count);
            }
        }

        return result;
    }

    private bool TryGetCandidates(AppDbContext db, out List<Notification> candidates)
    {
        candidates = [];
        if (!capabilities.IsEnabled(CapabilityCode)) return false;

        candidates = db.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(n => NotificationEmailPlanner.Classify(n) is not null)
            .ToList();

        return candidates.Count > 0;
    }

    private async Task EnqueueAsync(AppDbContext db, List<Notification> candidates, bool sync, CancellationToken ct)
    {
        async Task<List<T>> Load<T>(IQueryable<T> query) => sync ? query.ToList() : await query.ToListAsync(ct);

        string[] settingKeys =
        [
            EmailEnabledSettingKey, SmtpSettings.KeyMode, SmtpSettings.KeyHost,
            CompanyIdentity.NameKey, CompanyIdentity.LegacyNameKey,
        ];
        var settings = (await Load(db.SystemSettings.AsNoTracking()
                .Where(s => settingKeys.Contains(s.Key))
                .Select(s => new { s.Key, s.Value })))
            .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

        if (!IsEmailOn(settings))
            return;

        var userIds = candidates.Select(n => n.UserId).Distinct().ToList();
        var recipients = (await Load(db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new NotificationEmailRecipient(u.Id, u.Email, u.FirstName, u.IsActive))))
            .ToDictionary(r => r.UserId);

        var preferenceKeys = NotificationEmailPlanner.PreferenceKeys.ToList();
        var preferences = (await Load(db.UserPreferences.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId) && preferenceKeys.Contains(p.Key))
                .Select(p => new { p.UserId, p.Key, p.ValueJson })))
            .GroupBy(p => (p.UserId, p.Key))
            .ToDictionary(g => g.Key, g => g.First().ValueJson);

        var now = clock.UtcNow;
        var cutoff = now - NotificationEmailPlanner.CoalesceWindow;
        var prefix = NotificationEmailPlanner.OperationKeyPrefix;
        var recent = await Load(db.IntegrationOutboxEntries.AsNoTracking()
            .Where(e => e.Provider == IntegrationProvider.Email
                && e.CreatedAt >= cutoff
                && e.OperationKey.StartsWith(prefix))
            .Select(e => e.OperationKey));

        var planned = NotificationEmailPlanner.Plan(candidates, recipients, preferences, recent);
        if (planned.Count == 0)
            return;

        var companyName = CompanyName(settings);
        var appBaseUrl = configuration[FrontendBaseUrl.SettingKey];
        foreach (var email in planned)
        {
            var notification = email.Notification;
            var recordPath = notification.EntityType is { Length: > 0 } entityType && notification.EntityId is int entityId
                ? $"/open/{Uri.EscapeDataString(entityType)}/{entityId}"
                : null;
            var html = EmailTemplateBuilder.BuildRecordEventEmail(
                companyName,
                email.Recipient.FirstName,
                notification.Title,
                notification.Message,
                appBaseUrl,
                recordPath,
                email.Language);
            var message = new EmailMessage(email.Recipient.Email!, $"[{companyName}] {notification.Title}", html);
            var operationKey = $"{email.CoalesceKey}{Guid.NewGuid():N}";

            db.IntegrationOutboxEntries.Add(new IntegrationOutboxEntry
            {
                Provider = IntegrationProvider.Email,
                OperationKey = operationKey,
                IdempotencyKey = IntegrationOutboxService.BuildIdempotencyKey(IntegrationProvider.Email, operationKey),
                Payload = JsonSerializer.Serialize(message),
                Status = OutboxStatus.Pending,
                MaxAttempts = MaxAttempts,
                NextAttemptAt = now,
                EntityType = notification.EntityType,
                EntityId = notification.EntityId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static bool IsEmailOn(IReadOnlyDictionary<string, string> settings)
    {
        if (!string.Equals(settings.GetValueOrDefault(EmailEnabledSettingKey)?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            return false;

        var mode = settings.GetValueOrDefault(SmtpSettings.KeyMode)?.Trim();
        if (string.Equals(mode, IntegrationModeChoices.Disabled, StringComparison.OrdinalIgnoreCase))
            return false;

        return !string.Equals(mode, IntegrationModeChoices.Real, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(settings.GetValueOrDefault(SmtpSettings.KeyHost));
    }

    private static string CompanyName(IReadOnlyDictionary<string, string> settings)
    {
        var current = settings.GetValueOrDefault(CompanyIdentity.NameKey);
        if (!string.IsNullOrWhiteSpace(current)) return current.Trim();
        var legacy = settings.GetValueOrDefault(CompanyIdentity.LegacyNameKey);
        return string.IsNullOrWhiteSpace(legacy) ? "Forge" : legacy.Trim();
    }
}
