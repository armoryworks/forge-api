using Forge.Api.Features.Reports;
using Forge.Api.Features.ShopFloor;
using Forge.Data.Context;

namespace Forge.Api.Features.ScheduledTasks;

public static class ScheduledTaskCron
{
    public static async Task<DateTimeOffset?> NextRunAfterAsync(
        AppDbContext db, string cronExpression, DateTimeOffset afterUtc, CancellationToken ct)
    {
        if (!ReportScheduleCron.IsValid(cronExpression))
            return null;

        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        return ReportScheduleCron.NextOccurrence(cronExpression, afterUtc, zone);
    }
}
