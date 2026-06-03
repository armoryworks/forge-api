using Forge.Core.Enums.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// The resolver the accounting suite binds to for GL segregation-of-duties
/// (ACCOUNTING_SUITE_PLAN §5.7). It exposes the <b>effective</b> (resolved) GL
/// capability set of the server-trusted current principal — the transitive
/// closure of whatever roles/rollups compose into the caller's identity.
/// <para>
/// <b>Model-agnostic by design.</b> How roles compose (today's flat
/// <c>RoleTemplate</c> rollups expanded into JWT role claims, or a future
/// hierarchical/recursive role graph) is an orthogonal identity-system concern.
/// The accounting code only ever asks "does the current principal effectively
/// hold capability X?" — it never inspects role names. Per §5.7 the default
/// grants attach to <c>Controller</c>; bare <c>Admin</c>/<c>Manager</c>/
/// <c>OfficeManager</c> get no GL capability.
/// </para>
/// </summary>
public interface ICurrentUserCapabilities
{
    /// <summary>
    /// The server-trusted principal id (never client-supplied) recorded as
    /// <c>PostedBy</c>/<c>ApprovedBy</c> by the engine. Null when there is no
    /// authenticated user on the ambient context (e.g. a background job that
    /// has not established a principal).
    /// </summary>
    int? CurrentUserId { get; }

    /// <summary>
    /// True when the current principal effectively holds the given GL
    /// capability. Evaluated against the resolved/effective permission set, not
    /// against hard-coded role names.
    /// </summary>
    bool Has(GlCapability capability);

    /// <summary>
    /// SoD toxic-combination probe (§5.7): true when the current principal can
    /// effectively grant permissions (i.e. is an administrative principal) AND
    /// also holds <see cref="GlCapability.PostJournalEntry"/>. A solo
    /// <c>OwnerOperator</c> trips this intentionally and visibly; the checker
    /// exists to surface the <i>unintended</i> combinations (especially once a
    /// hierarchical role model can hide such combos inside the graph).
    /// </summary>
    bool HasToxicPostingCombination();
}
