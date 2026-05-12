using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Services;

public class IntegrationOutboxService(
    AppDbContext db,
    IClock clock,
    ILogger<IntegrationOutboxService> logger) : IIntegrationOutboxService
{
    public Task<IntegrationOutboxEntry> EnqueueEmailAsync(
        string operationKey,
        EmailMessage message,
        string? entityType = null,
        int? entityId = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(message);
        return EnqueueAsync(
            IntegrationProvider.Email,
            operationKey,
            payload,
            entityType,
            entityId,
            maxAttempts: 5,
            ct);
    }

    public async Task<IntegrationOutboxEntry> EnqueueAsync(
        IntegrationProvider provider,
        string operationKey,
        string payload,
        string? entityType = null,
        int? entityId = null,
        int maxAttempts = 5,
        CancellationToken ct = default)
    {
        var idempotencyKey = BuildIdempotencyKey(provider, operationKey);

        var existing = await db.IntegrationOutboxEntries
            .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, ct);

        if (existing != null)
        {
            logger.LogInformation(
                "Outbox enqueue no-op (idempotency): provider={Provider} key={Key} existingId={Id} status={Status}",
                provider, operationKey, existing.Id, existing.Status);
            return existing;
        }

        var entry = new IntegrationOutboxEntry
        {
            Provider = provider,
            OperationKey = operationKey,
            IdempotencyKey = idempotencyKey,
            Payload = payload,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            NextAttemptAt = clock.UtcNow,
            EntityType = entityType,
            EntityId = entityId,
        };

        db.IntegrationOutboxEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Outbox enqueued: provider={Provider} key={Key} id={Id}",
            provider, operationKey, entry.Id);

        return entry;
    }

    private static string BuildIdempotencyKey(IntegrationProvider provider, string operationKey)
    {
        var raw = $"{provider}:{operationKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
