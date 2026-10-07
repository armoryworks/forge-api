using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<List<UserResponseModel>> GetAllActiveAsync(
        Func<EmployeeProfile?, string?, bool> canBeAssignedJobs, CancellationToken ct)
        => ProjectAsync(db.Users.Where(u => u.IsActive), canBeAssignedJobs, ct);

    public Task<List<UserResponseModel>> FindByNamesAsync(
        IEnumerable<string> names, Func<EmployeeProfile?, string?, bool> canBeAssignedJobs, CancellationToken ct)
    {
        var nameList = names.Select(n => n.ToLower()).ToList();
        var users = db.Users
            .Where(u => u.IsActive)
            .Where(u => nameList.Contains(u.FirstName!.ToLower())
                     || nameList.Contains((u.FirstName + " " + u.LastName).Trim().ToLower().Replace(" ", "")));
        return ProjectAsync(users, canBeAssignedJobs, ct);
    }

    private async Task<List<UserResponseModel>> ProjectAsync(
        IQueryable<ApplicationUser> users,
        Func<EmployeeProfile?, string?, bool> canBeAssignedJobs,
        CancellationToken ct)
    {
        var rows = await users
            .AsNoTracking()
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                u.Id,
                u.Initials,
                u.FirstName,
                u.LastName,
                u.AvatarColor,
                u.IsNonEmployee,
                WorkLocationState = u.WorkLocation != null ? u.WorkLocation.State : null,
            })
            .ToListAsync(ct);

        var userIds = rows.Select(r => (int?)r.Id).ToList();
        var profiles = (await db.EmployeeProfiles
                .AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .ToListAsync(ct))
            .GroupBy(p => p.UserId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        return rows
            .Select(r => new UserResponseModel(
                r.Id,
                r.Initials ?? "??",
                (r.LastName + ", " + r.FirstName).Trim(',', ' '),
                r.AvatarColor ?? "#94a3b8",
                r.IsNonEmployee || canBeAssignedJobs(profiles.GetValueOrDefault(r.Id), r.WorkLocationState)))
            .ToList();
    }
}
