using System.Text.Json;

using MediatR;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 / WU-19 / F9 — un-link the User from an Employee while preserving
/// both records. The Employee remains; the User is deactivated (so audit
/// history and historical references stay valid) and the link is broken.
/// </summary>
public record RevokeSystemAccessCommand(int EmployeeId) : IRequest<bool>;

public class RevokeSystemAccessHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    ISystemAuditWriter audit,
    IHttpContextAccessor httpContext)
    : IRequestHandler<RevokeSystemAccessCommand, bool>
{
    public async Task<bool> Handle(RevokeSystemAccessCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId && p.DeletedAt == null, ct);

        if (profile is null) return false;
        if (profile.UserId is null) return true;

        var formerUserId = profile.UserId.Value;

        // Capture identity from User before unlink (so the Employee continues
        // to display a name even with no User account).
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == formerUserId, ct);
        if (user is not null)
        {
            if (string.IsNullOrEmpty(profile.FirstName)) profile.FirstName = user.FirstName;
            if (string.IsNullOrEmpty(profile.LastName)) profile.LastName = user.LastName;
            if (string.IsNullOrEmpty(profile.WorkEmail)) profile.WorkEmail = user.Email;

            user.IsActive = false;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);
        }

        profile.UserId = null;
        await db.SaveChangesAsync(ct);

        var actorId = TryGetActorId(httpContext);
        await audit.WriteAsync(
            "EmployeeUserUnlinked",
            actorId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new { formerUserId }),
            ct: ct);

        return true;
    }

    private static int TryGetActorId(IHttpContextAccessor http)
    {
        var v = http.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : 0;
    }
}
