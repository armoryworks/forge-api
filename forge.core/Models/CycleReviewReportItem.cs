namespace Forge.Core.Models;

public record CycleReviewReportItem(
    int CycleId,
    string CycleName,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    int TotalEntries,
    int CompletedEntries,
    decimal CompletionRate,
    int RolledOverCount);
