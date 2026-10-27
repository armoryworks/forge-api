namespace Forge.Core.Models;

public record CloudStorageTestResultModel(
    bool Success,
    string Message,
    long LatencyMs);
