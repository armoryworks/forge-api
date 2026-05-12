using Forge.Core.Entities;

namespace Forge.Core.Interfaces;

public interface ISyncQueueRepository
{
    Task<SyncQueueEntry> EnqueueAsync(string entityType, int entityId, string operation, string? payload, CancellationToken ct);
    Task<List<SyncQueueEntry>> GetPendingAsync(int batchSize, CancellationToken ct);
    Task MarkProcessingAsync(int id, CancellationToken ct);
    Task MarkCompletedAsync(int id, CancellationToken ct);
    Task MarkFailedAsync(int id, string errorMessage, CancellationToken ct);
    Task<int> GetQueueDepthAsync(CancellationToken ct);
    Task<int> GetFailedCountAsync(CancellationToken ct);
    Task RequeueFailedAsync(int maxAttempts, CancellationToken ct);
    Task PurgeCompletedAsync(int olderThanDays, CancellationToken ct);
}
