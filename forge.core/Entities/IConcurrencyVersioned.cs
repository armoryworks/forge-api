namespace Forge.Core.Entities;

/// <summary>
/// Marker for transactional entities that support optimistic locking via
/// monotonically-incrementing Version. Per Phase 1F decision: transactional
/// state (work orders, invoices, POs, payments, shipments, sales orders,
/// quotes) is locked; master data (parts, customers, vendors, etc.) is
/// last-write-wins.
///
/// Phase 3 / WU-11 / TODO E1.
/// Cases: CONC-OPTIMISTIC-LOCK-001.
/// </summary>
public interface IConcurrencyVersioned
{
    /// <summary>
    /// Monotonically-incrementing version. Starts at 1 on insert; bumped on
    /// every Modified save. Compared against the request's If-Match header on
    /// PATCH/PUT/DELETE. Mismatch → 412 Precondition Failed.
    /// </summary>
    uint Version { get; set; }
}
