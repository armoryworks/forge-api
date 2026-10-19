using Forge.Core.Models;

namespace Forge.Api.Features.Mrp;

public static class ForecastPeriod
{
    public static bool SameDate(DateTimeOffset left, DateTimeOffset right) =>
        left.UtcDateTime.Date == right.UtcDateTime.Date;

    public static bool IsForecast(ForecastBucketModel bucket, DateTimeOffset forecastStart) =>
        bucket.HistoricalQuantity is null && bucket.PeriodStart.UtcDateTime.Date >= forecastStart.UtcDateTime.Date;
}
