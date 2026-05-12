using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents;

public class DomainEventFailureService(AppDbContext db, IClock clock)
{
    public async Task LogFailure(string eventType, string payload, string handlerName, string error, CancellationToken ct)
    {
        db.DomainEventFailures.Add(new DomainEventFailure
        {
            EventType = eventType,
            EventPayload = payload,
            HandlerName = handlerName,
            ErrorMessage = error,
            FailedAt = clock.UtcNow,
            Status = DomainEventFailureStatus.Failed,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DomainEventFailure>> GetFailed(CancellationToken ct)
    {
        return await db.DomainEventFailures
            .Where(f => f.Status != DomainEventFailureStatus.Resolved)
            .OrderByDescending(f => f.FailedAt)
            .ToListAsync(ct);
    }

    public async Task<List<DomainEventFailure>> GetAll(CancellationToken ct)
    {
        return await db.DomainEventFailures
            .OrderByDescending(f => f.FailedAt)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task MarkResolved(int id, CancellationToken ct)
    {
        var failure = await db.DomainEventFailures.FindAsync([id], ct);
        if (failure is null) throw new KeyNotFoundException($"DomainEventFailure {id} not found");

        failure.Status = DomainEventFailureStatus.Resolved;
        failure.ResolvedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkRetrying(int id, CancellationToken ct)
    {
        var failure = await db.DomainEventFailures.FindAsync([id], ct);
        if (failure is null) throw new KeyNotFoundException($"DomainEventFailure {id} not found");

        failure.Status = DomainEventFailureStatus.Retrying;
        failure.RetryCount++;
        failure.LastRetryAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static string SerializeEvent(object notification)
    {
        try
        {
            return JsonSerializer.Serialize(notification, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return notification.ToString() ?? "unknown";
        }
    }
}
