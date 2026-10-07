using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.EmployeeProfile;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public static class AssigneeComplianceCheck
{
    public static async Task EnsureCanBeAssigned(
        AppDbContext db, ICapabilitySnapshotProvider capabilities, int userId, CancellationToken ct)
    {
        if (!EmployeeComplianceRules.IsHrTrackingOn(capabilities)) return;

        var isNonEmployee = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.IsNonEmployee)
            .FirstOrDefaultAsync(ct);

        if (isNonEmployee) return;

        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var stateCategory = await EmployeeComplianceRules.ResolveStateCategoryAsync(db, userId, ct);

        if (!EmployeeComplianceRules.CanBeAssignedJobs(profile, stateCategory, hrOn: true))
        {
            throw new InvalidOperationException(
                "User cannot be assigned to jobs — required compliance documents are incomplete " +
                "(W-4, I-9, State Withholding, or Emergency Contact).");
        }
    }
}
