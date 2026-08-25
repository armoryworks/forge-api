using System.Security.Claims;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Devices;

public record LockPolicyResponseModel(int IdleTimeoutMinutes);

public record GetLockPolicyQuery : IRequest<LockPolicyResponseModel>;

/// <summary>
/// Resolves the caller's local-lock idle timeout. Office-tier roles get the
/// short timeout, everyone else the floor timeout (a mixed-role user gets
/// the stricter of the two). Admin-configurable via system settings
/// mobile.idle_timeout_office_minutes / mobile.idle_timeout_floor_minutes;
/// defaults come from Mobile options (15 min office, 8 h floor).
/// </summary>
public class GetLockPolicyHandler(
    AppDbContext db,
    IOptions<MobileOptions> options,
    IHttpContextAccessor httpContext)
    : IRequestHandler<GetLockPolicyQuery, LockPolicyResponseModel>
{
    private static readonly string[] OfficeRoles =
        ["Admin", "Manager", "OfficeManager", "Controller"];

    public async Task<LockPolicyResponseModel> Handle(
        GetLockPolicyQuery request, CancellationToken cancellationToken)
    {
        var principal = httpContext.HttpContext!.User;
        var isOffice = OfficeRoles.Any(principal.IsInRole);

        var key = isOffice ? "mobile.idle_timeout_office_minutes" : "mobile.idle_timeout_floor_minutes";
        var fallback = isOffice
            ? options.Value.IdleTimeoutOfficeMinutes
            : options.Value.IdleTimeoutFloorMinutes;

        var setting = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        var minutes = setting is not null && int.TryParse(setting.Value, out var parsed) && parsed > 0
            ? parsed
            : fallback;

        return new LockPolicyResponseModel(minutes);
    }
}
