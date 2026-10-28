using System.Security.Claims;

namespace Forge.Api.Authorization;

/// <summary>
/// The roles that may accept a receipt, receive-back or shipment above its over-quantity tolerance: the roles
/// purchase-order approvals route to (Manager) and Admin.
/// </summary>
public static class ToleranceOverrideRoles
{
    public const string Override = "Admin,Manager";

    public static bool CanOverride(ClaimsPrincipal? user)
        => user is not null && Override.Split(',').Any(user.IsInRole);
}
