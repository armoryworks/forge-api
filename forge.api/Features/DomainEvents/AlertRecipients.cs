using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents;

internal static class AlertRecipients
{
    public static async Task<List<int>> ActiveUserIdsInRolesAsync(
        AppDbContext db, IReadOnlyCollection<string> roleNames, CancellationToken ct)
    {
        return await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Where(x => x.Name != null && roleNames.Contains(x.Name))
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new { u.Id, u.IsActive })
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(ct);
    }

    public static async Task<HashSet<int>> UsersWithOpenAlertAsync(
        AppDbContext db, string type, string entityType, int entityId, CancellationToken ct)
    {
        var userIds = await db.Notifications
            .Where(n => n.Type == type
                && n.EntityType == entityType
                && n.EntityId == entityId
                && !n.IsRead
                && !n.IsDismissed)
            .Select(n => n.UserId)
            .Distinct()
            .ToListAsync(ct);

        return [.. userIds];
    }
}
