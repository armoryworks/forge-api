using Forge.Core.Interfaces;

namespace Forge.Api.Jobs;

/// <summary>
/// Monthly Hangfire job — recalculates vendor scorecards for all active vendors
/// using the trailing 12-month period.
/// </summary>
public class RecalculateVendorScorecardsJob(
    IVendorScorecardService scorecardService,
    IClock clock,
    ILogger<RecalculateVendorScorecardsJob> logger)
{
    public async Task RecalculateAsync(CancellationToken ct = default)
    {
        var periodEnd = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var periodStart = periodEnd.AddMonths(-12);

        logger.LogInformation(
            "Starting monthly vendor scorecard recalculation for period {Start} to {End}",
            periodStart, periodEnd);

        await scorecardService.RecalculateAllAsync(periodStart, periodEnd, ct);

        logger.LogInformation("Completed monthly vendor scorecard recalculation");
    }
}
