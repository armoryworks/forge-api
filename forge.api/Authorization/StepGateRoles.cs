using System.Security.Claims;

namespace Forge.Api.Authorization;

/// <summary>
/// The roles that may override a blocked step gate (<c>CAP-MFG-GATE-OVERRIDE</c>): the shop's supervisors
/// (Manager), Production Manager, and Admin.
/// </summary>
public static class StepGateRoles
{
    public const string Override = "Admin,Manager,Production Manager";

    public static bool CanOverride(ClaimsPrincipal? user)
        => user is not null && Override.Split(',').Any(user.IsInRole);
}
