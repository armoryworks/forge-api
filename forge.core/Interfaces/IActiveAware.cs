namespace Forge.Core.Interfaces;

/// <summary>
/// Master-data entities (Vendor, Customer, Part, Asset, etc.) implement this
/// so the shared <c>ActiveCheck</c> validator can reject mutating-transaction
/// requests that target a deactivated/retired/obsolete record.
///
/// Phase 1 found that <c>isActive=false</c> on a vendor persists, the UI shows
/// the vendor as inactive, but POST /purchase-orders against the deactivated
/// vendor still returned 201 — there was no active-check in the PO-create
/// path. The same anti-pattern is suspected on the customer/part/asset edges.
/// This interface gives those edges one shared check rather than scattering
/// per-handler boolean tests. (Phase 3 H2 / WU-12.)
/// </summary>
public interface IActiveAware
{
    /// <summary>
    /// True when the master-data record is currently usable on new
    /// transactions. False when the record has been deactivated / retired /
    /// archived / marked obsolete and may only participate in in-flight
    /// transactions issued before its deactivation.
    /// </summary>
    bool IsActiveForNewTransactions { get; }

    /// <summary>
    /// Human-readable identifier shown back to the caller in the validation
    /// error envelope (e.g. "Acme Industries" / "Part PN-1234"). The active
    /// check uses this to name the offending record so the operator can
    /// identify it in the UI without an extra round trip.
    /// </summary>
    string GetDisplayName();
}
