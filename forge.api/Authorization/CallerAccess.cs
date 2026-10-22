using System.Security.Claims;

using Forge.Api.Middleware;

namespace Forge.Api.Authorization;

/// <summary>
/// Owner-or-supervisor checks for per-person records (time, attendance, leave):
/// Admin and Manager act on anyone's rows, OfficeManager and Controller read anyone's rows
/// for payroll, everyone else reads and changes only their own.
/// </summary>
public static class CallerAccess
{
    public static int UserId(ClaimsPrincipal? user)
        => int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static bool IsSupervisor(ClaimsPrincipal? user)
        => user?.IsInRole("Admin") == true || user?.IsInRole("Manager") == true;

    public static bool CanReadOthers(ClaimsPrincipal? user)
        => IsSupervisor(user) || user?.IsInRole("OfficeManager") == true || user?.IsInRole("Controller") == true;

    public static int? ScopeReadToSelf(ClaimsPrincipal? user, int? requestedUserId, string forbiddenMessage)
    {
        if (CanReadOthers(user))
            return requestedUserId;

        var callerId = UserId(user);
        if (requestedUserId.HasValue && requestedUserId.Value != callerId)
            throw new ForbiddenException(forbiddenMessage);

        return callerId;
    }
}
