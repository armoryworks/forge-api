
namespace Forge.Core.Models;

public record DemandForecastResponseModel(
    int Id,
    string Name,
    int PartId,
    string PartNumber,
    string? PartDescription,
    ForecastMethod Method,
    ForecastStatus Status,
    int HistoricalPeriods,
    int ForecastPeriods,
    double? SmoothingFactor,
    DateTimeOffset ForecastStartDate,
    List<ForecastBucketModel>? ForecastBuckets,
    int? AppliedToMasterScheduleId,
    int OverrideCount,
    DateTimeOffset CreatedAt
);

public record ForecastBucketModel(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal ForecastedQuantity,
    decimal? HistoricalQuantity,
    decimal? OverrideQuantity
);

public record ForecastOverrideResponseModel(
    int Id,
    int DemandForecastId,
    DateTimeOffset PeriodStart,
    decimal OriginalQuantity,
    decimal OverrideQuantity,
    string? Reason,
    int? OverriddenByUserId
);
