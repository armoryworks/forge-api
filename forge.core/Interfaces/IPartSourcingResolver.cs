using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Pillar 3 — resolves the effective per-part sourcing values
/// (<c>LeadTimeDays</c> / <c>MinOrderQty</c> / <c>PackSize</c>) by
/// preferring the preferred <c>VendorPart</c> row's columns when one
/// exists and falling back to the <c>Part</c> snapshot columns otherwise.
///
/// Coalescing is per-column — a preferred VendorPart row with a non-null
/// override on one column and a null on another will provide the
/// override and fall back to the snapshot independently.
///
/// Use this from sourcing-decision contexts (auto-PO sizing, MRP supply
/// windows, replenishment cover-day calculations). UI-bound list/detail
/// projections that simply display the part snapshot are fine to keep
/// reading <c>Part</c> directly — those will be migrated opportunistically.
/// </summary>
public interface IPartSourcingResolver
{
    /// <summary>
    /// Resolve effective sourcing values for a single part.
    /// </summary>
    Task<PartSourcingValues> ResolveAsync(int partId, CancellationToken ct);

    /// <summary>
    /// Bulk variant — issues a single VendorPart query for all
    /// requested ids, then a single Parts query for the snapshot
    /// fallback. Use this in MRP / auto-PO loops over many parts.
    /// </summary>
    /// <returns>Map keyed by partId. Includes one entry per requested id.</returns>
    Task<IReadOnlyDictionary<int, PartSourcingValues>> ResolveManyAsync(
        IReadOnlyCollection<int> partIds, CancellationToken ct);
}
