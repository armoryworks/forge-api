using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Manager team restriction for the employee endpoints: an Admin, or a caller with no team,
/// sees everyone; any other caller sees only users on their own team and no User-less profiles.
/// </summary>
public static class EmployeeTeamScope
{
    public static async Task<int?> RestrictedTeamIdAsync(
        AppDbContext db, int? callerUserId, bool callerIsAdmin, CancellationToken ct)
    {
        if (callerIsAdmin || !callerUserId.HasValue)
            return null;

        return await db.Users
            .Where(u => u.Id == callerUserId.Value)
            .Select(u => u.TeamId)
            .FirstOrDefaultAsync(ct);
    }
}
