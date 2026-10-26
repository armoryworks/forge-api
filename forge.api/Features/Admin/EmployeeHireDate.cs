using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public static class EmployeeHireDate
{
    public static async Task SetAsync(AppDbContext db, int userId, DateTimeOffset hireDate, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new Core.Entities.EmployeeProfile { UserId = userId };
            db.EmployeeProfiles.Add(profile);
        }

        profile.StartDate = hireDate;
    }
}
