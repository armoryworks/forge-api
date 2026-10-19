using Forge.Api.Features.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily fiscal-calendar rollover: makes sure every active book has a fiscal year covering the next month,
/// so postings dated after the current year ends find an open period (see <see cref="FiscalCalendar"/>).
/// </summary>
public class FiscalCalendarRolloverJob(
    AppDbContext db,
    IClock clock,
    ILogger<FiscalCalendarRolloverJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var through = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddMonths(1);
        var added = await FiscalCalendar.EnsureCoverageAsync(db, through, ct);
        logger.LogInformation("Fiscal-calendar rollover: added {Count} fiscal years (coverage through {Through}).", added, through);
    }
}
