using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Forge.Api.Capabilities;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Services;

namespace Forge.Api.Features.Notifications;

public sealed class NotificationChatFanOutInterceptor(
    ICapabilitySnapshotProvider capabilities,
    IClock clock,
    ILogger<NotificationChatFanOutInterceptor> logger) : SaveChangesInterceptor
{
    public const string CapabilityCode = "CAP-EXT-CHAT-INTEGRATION";
    public const string MessagingCategory = "messaging";
    private const int MaxAttempts = 5;

    public static readonly IReadOnlySet<string> ChatProviderIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "slack", "teams", "discord", "google_chat" };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db && TryGetNewNotifications(db, out var added))
        {
            try
            {
                var userIds = added.Select(n => n.UserId).Distinct().ToList();
                Enqueue(db, added, IntegrationsQuery(db, userIds).ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Chat fan-out skipped for {Count} notification(s)", added.Count);
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db && TryGetNewNotifications(db, out var added))
        {
            try
            {
                var userIds = added.Select(n => n.UserId).Distinct().ToList();
                Enqueue(db, added, await IntegrationsQuery(db, userIds).ToListAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Chat fan-out skipped for {Count} notification(s)", added.Count);
            }
        }

        return result;
    }

    private bool TryGetNewNotifications(AppDbContext db, out List<Notification> added)
    {
        added = [];
        if (!capabilities.IsEnabled(CapabilityCode)) return false;

        added = db.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        return added.Count > 0;
    }

    private static IQueryable<UserIntegration> IntegrationsQuery(AppDbContext db, List<int> userIds) =>
        db.UserIntegrations
            .AsNoTracking()
            .Where(i => i.IsActive
                && i.Category == MessagingCategory
                && userIds.Contains(i.UserId));

    private void Enqueue(AppDbContext db, List<Notification> notifications, List<UserIntegration> integrations)
    {
        var chatIntegrations = integrations.Where(i => ChatProviderIds.Contains(i.ProviderId)).ToList();
        if (chatIntegrations.Count == 0) return;

        var now = clock.UtcNow;
        foreach (var notification in notifications)
        {
            foreach (var integration in chatIntegrations.Where(i => i.UserId == notification.UserId))
            {
                var operationKey = $"chat:{integration.Id}:{Guid.NewGuid():N}";
                var payload = new ChatNotificationOutboxPayload(
                    notification.UserId,
                    integration.Id,
                    integration.ProviderId,
                    notification.Title,
                    notification.Message,
                    notification.Severity,
                    notification.EntityType,
                    notification.EntityId);

                db.IntegrationOutboxEntries.Add(new IntegrationOutboxEntry
                {
                    Provider = IntegrationProvider.Messaging,
                    OperationKey = operationKey,
                    IdempotencyKey = IntegrationOutboxService.BuildIdempotencyKey(IntegrationProvider.Messaging, operationKey),
                    Payload = JsonSerializer.Serialize(payload),
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
    }
}
