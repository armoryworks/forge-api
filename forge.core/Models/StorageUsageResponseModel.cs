namespace Forge.Core.Models;

public record StorageUsageResponseModel(
    string EntityType,
    int FileCount,
    long TotalSizeBytes);
