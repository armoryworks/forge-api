using System.Text.Json;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 / WU-19 / F9 — soft-delete an Employee record by
/// <c>EmployeeProfile.Id</c>. If a User is linked, the User is left in place
/// (deactivated) so audit history is preserved.
/// </summary>
public record DeleteEmployeeCommand(int EmployeeId) : IRequest<bool>;

public class DeleteEmployeeHandler(
    AppDbContext db,
    ISystemAuditWriter audit,
    IHttpContextAccessor httpContext)
    : IRequestHandler<DeleteEmployeeCommand, bool>
{
    public async Task<bool> Handle(DeleteEmployeeCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.Id == request.EmployeeId && p.DeletedAt == null, ct);

        if (profile is null) return false;

        profile.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var actorId = TryGetActorId(httpContext);
        await audit.WriteAsync(
            "EmployeeDeleted",
            actorId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new { hadUser = profile.UserId.HasValue }),
            ct: ct);

        return true;
    }

    private static int TryGetActorId(IHttpContextAccessor http)
    {
        var v = http.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : 0;
    }
}
