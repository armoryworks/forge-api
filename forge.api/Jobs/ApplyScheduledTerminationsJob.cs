using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Employees;
using Forge.Api.Features.ShopFloor;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

/// <summary>
/// Daily Hangfire job — applies terminations whose effective date has arrived in the shop
/// time zone and that have not been applied yet. A termination that cannot be applied (for
/// example the last active Admin) is logged and retried the next day.
/// </summary>
public class ApplyScheduledTerminationsJob(
    AppDbContext db,
    EmployeeTermination termination,
    IClock clock,
    ILogger<ApplyScheduledTerminationsJob> logger)
{
    public async Task ApplyDueAsync(CancellationToken ct = default)
    {
        var zone = await ClockStateRules.ShopTimeZoneAsync(db, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);

        var due = await db.EmployeeProfiles.AsNoTracking()
            .Where(p => p.TerminationDate != null && p.TerminationDate <= today && p.TerminationAppliedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .Take(500)
            .ToListAsync(ct);

        foreach (var profileId in due)
        {
            try
            {
                var profile = await db.EmployeeProfiles.FirstAsync(p => p.Id == profileId, ct);
                await termination.ApplyAsync(profile, profile.TerminatedByUserId ?? 0, ct);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "[ApplyScheduledTerminations] Could not apply termination for profile {ProfileId}", profileId);
                db.ChangeTracker.Clear();
            }
        }

        logger.LogInformation("[ApplyScheduledTerminations] {Count} termination(s) due", due.Count);
    }
}
