using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<List<UserResponseModel>> GetAllActiveAsync(CancellationToken ct)
    {
        return await db.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .GroupJoin(
                db.EmployeeProfiles,
                u => u.Id,
                p => p.UserId,
                (u, profiles) => new { User = u, Profile = profiles.FirstOrDefault() })
            .Select(x => new UserResponseModel(
                x.User.Id,
                x.User.Initials ?? "??",
                (x.User.LastName + ", " + x.User.FirstName).Trim(',', ' '),
                x.User.AvatarColor ?? "#94a3b8",
                x.Profile != null &&
                    x.Profile.W4CompletedAt != null &&
                    x.Profile.I9CompletedAt != null &&
                    x.Profile.StateWithholdingCompletedAt != null &&
                    !string.IsNullOrWhiteSpace(x.Profile.EmergencyContactName) &&
                    !string.IsNullOrWhiteSpace(x.Profile.EmergencyContactPhone)))
            .ToListAsync(ct);
    }

    public async Task<List<UserResponseModel>> FindByNamesAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var nameList = names.Select(n => n.ToLower()).ToList();
        return await db.Users
            .Where(u => u.IsActive)
            .Where(u => nameList.Contains(u.FirstName!.ToLower())
                     || nameList.Contains((u.FirstName + " " + u.LastName).Trim().ToLower().Replace(" ", "")))
            .GroupJoin(
                db.EmployeeProfiles,
                u => u.Id,
                p => p.UserId,
                (u, profiles) => new { User = u, Profile = profiles.FirstOrDefault() })
            .Select(x => new UserResponseModel(
                x.User.Id,
                x.User.Initials ?? "??",
                (x.User.LastName + ", " + x.User.FirstName).Trim(',', ' '),
                x.User.AvatarColor ?? "#94a3b8",
                x.Profile != null &&
                    x.Profile.W4CompletedAt != null &&
                    x.Profile.I9CompletedAt != null &&
                    x.Profile.StateWithholdingCompletedAt != null &&
                    !string.IsNullOrWhiteSpace(x.Profile.EmergencyContactName) &&
                    !string.IsNullOrWhiteSpace(x.Profile.EmergencyContactPhone)))
            .ToListAsync(ct);
    }
}
