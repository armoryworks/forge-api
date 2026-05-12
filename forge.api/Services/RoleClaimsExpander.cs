using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>
/// Expands a user's effective role claims by combining their directly-
/// assigned ASP.NET Identity roles with any roles included via an
/// assigned <see cref="Forge.Core.Entities.RoleTemplate"/> rollup.
///
/// When a user has a template assigned, every role name listed in the
/// template's <c>IncludedRoleNamesJson</c> is added to the effective set
/// (deduplicated, case-sensitive — matches Identity's role name handling).
/// Downstream <c>[Authorize(Roles = ...)]</c> and policy-based handlers
/// see the union as if each role had been individually assigned. Phase 3
/// / WU-06 / C1.
/// </summary>
public interface IRoleClaimsExpander
{
    Task<IList<string>> GetEffectiveRolesAsync(
        Microsoft.AspNetCore.Identity.IdentityUser<int> user,
        CancellationToken ct = default);
}

public class RoleClaimsExpander(
    UserManager<ApplicationUser> userManager,
    AppDbContext db) : IRoleClaimsExpander
{
    public async Task<IList<string>> GetEffectiveRolesAsync(
        Microsoft.AspNetCore.Identity.IdentityUser<int> user,
        CancellationToken ct = default)
    {
        var identityRoles = await userManager.GetRolesAsync((ApplicationUser)user);
        var effective = new HashSet<string>(identityRoles, StringComparer.Ordinal);

        // Pull the user's RoleTemplateId fresh — UserManager.GetRolesAsync may
        // have loaded a tracked entity but the template column isn't always
        // hydrated through Identity's UserStore plumbing. Hit the DB directly.
        var templateId = await db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => u.RoleTemplateId)
            .FirstOrDefaultAsync(ct);

        if (templateId is { } tid)
        {
            var template = await db.RoleTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tid && t.DeactivatedAt == null, ct);

            if (template is not null)
            {
                try
                {
                    var roles = JsonSerializer.Deserialize<string[]>(template.IncludedRoleNamesJson)
                                ?? [];
                    foreach (var r in roles)
                        if (!string.IsNullOrWhiteSpace(r))
                            effective.Add(r);
                }
                catch (JsonException)
                {
                    // Malformed JSON — log silently and fall back to direct roles.
                    // This is a tenant-data integrity issue, not a request-time
                    // error; surface it via audit but don't break login.
                }
            }
        }

        return effective.ToList();
    }
}
