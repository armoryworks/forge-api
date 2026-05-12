namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — effective sourcing values for a part. Resolved by
/// <c>IPartSourcingResolver</c> as: prefer the preferred VendorPart row's
/// columns when one exists, fall back to the Part snapshot columns
/// otherwise. Per-column coalescing — if the preferred VendorPart row has
/// a NULL column, that single column falls back to the Part snapshot.
/// </summary>
/// <param name="PartId">The part being resolved.</param>
/// <param name="PreferredVendorId">
/// Vendor id of the preferred VendorPart row, or <c>null</c> when no
/// preferred VendorPart exists (fully snapshot-driven).
/// </param>
/// <param name="LeadTimeDays">Effective lead time in days.</param>
/// <param name="MinOrderQty">Effective minimum order quantity.</param>
/// <param name="PackSize">Effective pack size.</param>
/// <param name="ResolvedFromVendorPart">
/// True if any of the values came from a VendorPart row (vs the Part
/// snapshot). Useful for diagnostics/observability — e.g., logging that
/// a value came from the per-vendor override.
/// </param>
public record PartSourcingValues(
    int PartId,
    int? PreferredVendorId,
    int? LeadTimeDays,
    decimal? MinOrderQty,
    decimal? PackSize,
    bool ResolvedFromVendorPart);
