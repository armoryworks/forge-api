using Forge.Core.Enums.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// Segregation-of-duties enforcement at the <see cref="IPostingEngine"/>
/// boundary (ACCOUNTING_SUITE_PLAN §5.7). Every GL mutation is gated here
/// against the caller's <b>effective</b> capability set
/// (<see cref="ICurrentUserCapabilities"/>) before the engine touches the
/// ledger. Authorization is capability-based, never role-name-based.
/// <para>
/// <b>Fail-safe default-deny:</b> an implementation that cannot resolve the
/// caller's capabilities (no ambient principal, mis-wired identity system)
/// MUST deny. The only exception is the explicit Phase-0 "dark" seam: when no
/// authorizer is injected into the engine at all, the engine treats the seam
/// as not-yet-wired and proceeds (CAP-ACCT-FULLGL is OFF so nothing reaches the
/// engine in production anyway). See <c>ForgeGlPostingEngine</c>'s TODO.
/// </para>
/// </summary>
public interface IGlBoundaryAuthorizer
{
    /// <summary>
    /// Throws <see cref="Forge.Core.Models.Accounting.GlAuthorizationException"/>
    /// when the current principal does not effectively hold
    /// <paramref name="capability"/>. Returns normally when authorized.
    /// </summary>
    void EnsureAuthorized(GlCapability capability);
}
