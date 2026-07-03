using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// compliance-calendar A-2 enforcement. Computes visible Super-Group ids from the
/// current user's roles + the per-group role allow-list. Capabilities gate feature
/// visibility elsewhere; this enforces per-group read policy.
/// </summary>
public sealed class CalendarVisibilityService(AppDbContext db) : ICalendarVisibilityService
{
    public async Task<IReadOnlyList<int>?> GetVisibleSuperGroupIdsAsync(CancellationToken ct = default)
    {
        var userId = db.CurrentUserId;
        if (userId is null)
            return null; // system / background context — unrestricted

        var roles = await db.UserRoles
            .Where(ur => ur.UserId == userId.Value)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(ct);

        if (roles.Contains("Admin"))
            return null; // Admin sees every group

        var visible = await db.CalendarSuperGroups
            .Where(g => g.DefaultVisible
                || db.CalendarSuperGroupRoleVisibilities.Any(v => v.SuperGroupId == g.Id && roles.Contains(v.Role)))
            .Select(g => g.Id)
            .ToListAsync(ct);

        return visible;
    }
}
