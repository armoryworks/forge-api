namespace Forge.Core.Models;

public record PerformanceReviewDetailResponseModel(
    PerformanceReviewResponseModel Review,
    IReadOnlyList<PerformanceReviewHistoryItemModel> History);
