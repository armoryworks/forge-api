namespace Forge.Core.Models;

public record AccountingSyncStatus(
    bool Connected,
    DateTimeOffset? LastSyncAt,
    int QueueDepth,
    int FailedCount);
