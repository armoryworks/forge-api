using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class SyncQueueRepository(AppDbContext db) : ISyncQueueRepository
{
    private const int MaxAttemptCount = 5;

    public async Task<SyncQueueEntry> EnqueueAsync(
        string entityType,
        int entityId,
        string operation,
        string? payload,
        CancellationToken ct)
    {
        var entry = new SyncQueueEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            Payload = payload,
            Status = SyncStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await db.SyncQueueEntries.AddAsync(entry, ct);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<List<SyncQueueEntry>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var entries = await db.SyncQueueEntries
            .Where(e => e.Status == SyncStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return entries;
        }

        foreach (var entry in entries)
            entry.Status = SyncStatus.Processing;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return entries;
    }

    public async Task MarkProcessingAsync(int id, CancellationToken ct)
    {
        var entry = await FindRequiredAsync(id, ct);
        entry.Status = SyncStatus.Processing;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkCompletedAsync(int id, CancellationToken ct)
    {
        var entry = await FindRequiredAsync(id, ct);
        entry.Status = SyncStatus.Completed;
        entry.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(int id, string errorMessage, CancellationToken ct)
    {
        var entry = await FindRequiredAsync(id, ct);
        entry.AttemptCount++;
        entry.ErrorMessage = errorMessage;

        // Retry automatically if under the attempt cap; otherwise leave as Failed
        entry.Status = entry.AttemptCount < MaxAttemptCount
            ? SyncStatus.Pending
            : SyncStatus.Failed;

        await db.SaveChangesAsync(ct);
    }

    public Task<int> GetQueueDepthAsync(CancellationToken ct)
        => db.SyncQueueEntries.CountAsync(e => e.Status == SyncStatus.Pending, ct);

    public Task<int> GetFailedCountAsync(CancellationToken ct)
        => db.SyncQueueEntries.CountAsync(e => e.Status == SyncStatus.Failed, ct);

    public async Task RequeueFailedAsync(int maxAttempts, CancellationToken ct)
    {
        var entries = await db.SyncQueueEntries
            .Where(e => e.Status == SyncStatus.Failed && e.AttemptCount < maxAttempts)
            .ToListAsync(ct);

        foreach (var entry in entries)
            entry.Status = SyncStatus.Pending;

        if (entries.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    public async Task PurgeCompletedAsync(int olderThanDays, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays);

        // Hard delete — sync queue entries are operational data, not business records
        var entries = await db.SyncQueueEntries
            .Where(e => e.Status == SyncStatus.Completed && e.ProcessedAt < cutoff)
            .ToListAsync(ct);

        if (entries.Count > 0)
        {
            db.SyncQueueEntries.RemoveRange(entries);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<SyncQueueEntry> FindRequiredAsync(int id, CancellationToken ct)
    {
        return await db.SyncQueueEntries.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"SyncQueueEntry {id} not found.");
    }
}
