using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Admin;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Resolves the employee profile behind an employee-detail id with the same order as
/// <see cref="GetEmployeeDetailHandler"/> (a user-less profile id, then a user id, then a linked
/// profile id; <c>kind</c> narrows it). A user with no profile row gets a new tracked profile.
/// A non-Admin caller reaches only users on their own team and never user-less profiles; a
/// caller may not act on their own record unless <c>refuseSelf</c> is false. A non-Admin caller
/// may not act on a user who holds the Admin role unless <c>refuseAdminTarget</c> is false.
/// </summary>
public static class EmployeeTarget
{
    public static async Task<Core.Entities.EmployeeProfile> ResolveAsync(
        AppDbContext db, int id, string? kind, int callerUserId, bool callerIsAdmin, CancellationToken ct, bool refuseSelf = true, bool refuseAdminTarget = true)
    {
        var normalized = kind?.Trim().ToLowerInvariant();
        var byProfile = normalized is null or EmployeeKinds.Profile;
        var byUser = normalized is null or EmployeeKinds.User;

        Core.Entities.EmployeeProfile? profile = null;
        if (byProfile)
            profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.Id == id && p.UserId == null, ct);

        if (profile is null && byUser && await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == id, ct);
            if (profile is null)
            {
                profile = new Core.Entities.EmployeeProfile { UserId = id };
                db.EmployeeProfiles.Add(profile);
            }
        }

        if (profile is null && byProfile)
            profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.Id == id && p.UserId != null, ct);

        if (profile is null)
            throw new KeyNotFoundException($"Employee {id} not found");

        var restrictedTeamId = await EmployeeTeamScope.RestrictedTeamIdAsync(db, callerUserId, callerIsAdmin, ct);
        if (restrictedTeamId is not null)
        {
            var visible = profile.UserId is int userId
                && await db.Users.AnyAsync(u => u.Id == userId && u.TeamId == restrictedTeamId, ct);
            if (!visible)
                throw new KeyNotFoundException($"Employee {id} not found");
        }

        if (refuseSelf && profile.UserId == callerUserId && callerUserId != 0)
            throw new ForbiddenException("You cannot terminate or rehire your own employment record.");

        if (refuseAdminTarget && !callerIsAdmin && profile.UserId is int targetId)
        {
            var adminRoleIds = db.Roles.Where(r => r.Name == UserDeactivation.AdminRole).Select(r => r.Id);
            if (await db.UserRoles.AnyAsync(ur => ur.UserId == targetId && adminRoleIds.Contains(ur.RoleId), ct))
                throw new ForbiddenException("Only an Admin can terminate or rehire a user who holds the Admin role.");
        }

        return profile;
    }
}
