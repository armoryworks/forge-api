namespace Forge.Core.Models;

public record PerformanceReviewHistoryItemModel(
    string Action,
    string Description,
    string? UserName,
    DateTimeOffset CreatedAt);
