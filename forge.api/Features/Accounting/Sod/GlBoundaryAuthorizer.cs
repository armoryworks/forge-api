using Microsoft.Extensions.Logging;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting.Sod;

/// <summary>
/// Default segregation-of-duties enforcer at the <see cref="IPostingEngine"/>
/// boundary (ACCOUNTING_SUITE_PLAN §5.7). Resolves the caller's effective GL
/// capabilities via <see cref="ICurrentUserCapabilities"/> and denies any GL
/// mutation the caller doesn't hold.
/// <para>
/// <b>Fail-safe default-deny.</b> If no authenticated principal can be resolved
/// (<see cref="ICurrentUserCapabilities.CurrentUserId"/> is null), every check
/// denies — a mis-wired identity context must never silently authorize a
/// posting. The SoD toxic-combination probe (§5.7) is logged (not blocked) so
/// the seeded <c>OwnerOperator</c> superuser keeps working while the
/// <i>unintended</i> Admin+Post combinations stay visible in the log.
/// </para>
/// </summary>
public sealed class GlBoundaryAuthorizer(
    ICurrentUserCapabilities capabilities,
    ILogger<GlBoundaryAuthorizer> logger) : IGlBoundaryAuthorizer
{
    public void EnsureAuthorized(GlCapability capability)
    {
        // Fail-safe: no resolvable principal → deny.
        if (capabilities.CurrentUserId is null)
            throw new GlAuthorizationException(
                capability,
                $"GL operation requiring {capability} denied: no authenticated principal on the request context.");

        if (!capabilities.Has(capability))
            throw new GlAuthorizationException(
                capability,
                $"Current principal {capabilities.CurrentUserId} does not hold the GL capability {capability}.");

        // Toxic-combination surfacing (§5.7) — warn, do not block. A solo
        // OwnerOperator legitimately wears all hats; the log catches the
        // unintended Admin+Post combinations.
        if (capability == GlCapability.PostJournalEntry && capabilities.HasToxicPostingCombination())
            logger.LogWarning(
                "[GL-SOD] Principal {UserId} holds a toxic combination (grant-permissions + POST_JE) while posting. " +
                "Expected only for a solo OwnerOperator; investigate if this is an unintended rollup.",
                capabilities.CurrentUserId);
    }
}
