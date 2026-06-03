using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Accounting.Sod;

/// <summary>
/// Default <see cref="ICurrentUserCapabilities"/> resolver for Forge's current
/// identity system (ACCOUNTING_SUITE_PLAN §5.7). It reads the server-trusted
/// JWT principal off the ambient <see cref="IHttpContextAccessor"/> and maps the
/// caller's <b>effective</b> role claims to GL capabilities.
/// <para>
/// "Effective" is already satisfied upstream: <c>RoleClaimsExpander</c> expands
/// any assigned <c>RoleTemplate</c> rollup into individual JWT role claims at
/// login, so <see cref="ClaimsPrincipal.IsInRole(string)"/> here sees the
/// transitive closure (e.g. the seeded <c>OwnerOperator</c> template
/// <c>["Admin","Manager","Controller"]</c> presents the <c>Controller</c> role
/// claim, and the back-office template likewise). We therefore only ask "does
/// the principal hold the Controller role claim?" — no role <i>names</i> leak
/// into the accounting engine; this class is the one place the mapping lives,
/// and it is swappable for a future hierarchical role graph without touching
/// the engine.
/// </para>
/// <para>
/// Per §5.7 the GL capabilities attach to <c>Controller</c>. Bare
/// <c>Admin</c>/<c>Manager</c>/<c>OfficeManager</c> get no GL capability
/// (toxic-combination avoidance) — they reach the books only through a rollup
/// that includes <c>Controller</c>.
/// </para>
/// </summary>
public sealed class CurrentUserCapabilities(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserCapabilities
{
    // The single role that GL capabilities attach to (§5.7). Composed roles
    // (OwnerOperator, back-office) present this claim via RoleClaimsExpander.
    private const string ControllerRole = "Controller";

    // Administrative / grant-permissions principals for the SoD toxic-combination
    // probe (§5.7). These are NOT a GL grant — they are the "can grant
    // permissions" side of the toxic pair.
    private static readonly string[] GrantPermissionRoles = ["Admin"];

    public int? CurrentUserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool Has(GlCapability capability)
    {
        var principal = Principal;
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        // Phase-0 default grant map: every GL capability attaches to Controller.
        // The switch is exhaustive so a future capability that should NOT map to
        // Controller (or maps to a new bookkeeping role) is a deliberate edit
        // here, not a silent grant.
        return capability switch
        {
            GlCapability.PostJournalEntry
            or GlCapability.ApproveJournalEntry
            or GlCapability.ReverseJournalEntry
            or GlCapability.ClosePeriodSoft
            or GlCapability.ClosePeriodHard
            or GlCapability.ReopenPeriod
            or GlCapability.ConfigureGl
                => principal.IsInRole(ControllerRole),
            _ => false, // fail-safe: unknown capability is denied
        };
    }

    public bool HasToxicPostingCombination()
    {
        var principal = Principal;
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var canGrant = GrantPermissionRoles.Any(principal.IsInRole);
        return canGrant && Has(GlCapability.PostJournalEntry);
    }

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
}
