using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Leads;

/// <summary>
/// The rep a lead is assigned to. Requests carry a user id, with 0 meaning "unassign"; anything else
/// must be an active user, so a lead is never handed to a deactivated or unknown account.
/// </summary>
public static class LeadOwner
{
    public static async Task<(int? UserId, string? Name)> ResolveAsync(AppDbContext db, int requestedUserId, CancellationToken ct)
    {
        if (requestedUserId == 0)
            return (null, null);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == requestedUserId && u.IsActive)
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException("The selected owner is not an active user.");

        return (user.Id, user.Name);
    }
}
