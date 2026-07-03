using Microsoft.AspNetCore.Identity;

using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Resolves a user's effective role claims from their directly-assigned
/// ASP.NET Identity roles.
///
/// <para>
/// Users now carry multiple Identity roles directly (multi-role assignment).
/// The prior rollup-template expansion was retired when the user-side
/// <c>RoleTemplate</c> coupling was removed — a user assigned several hats
/// simply holds each role. The interface is kept as the single seam every
/// login/token path calls, so token generation stays uniform.
/// </para>
/// </summary>
public interface IRoleClaimsExpander
{
    Task<IList<string>> GetEffectiveRolesAsync(
        Microsoft.AspNetCore.Identity.IdentityUser<int> user,
        CancellationToken ct = default);
}

public class RoleClaimsExpander(
    UserManager<ApplicationUser> userManager) : IRoleClaimsExpander
{
    public async Task<IList<string>> GetEffectiveRolesAsync(
        Microsoft.AspNetCore.Identity.IdentityUser<int> user,
        CancellationToken ct = default)
    {
        var identityRoles = await userManager.GetRolesAsync((ApplicationUser)user);
        return identityRoles
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
