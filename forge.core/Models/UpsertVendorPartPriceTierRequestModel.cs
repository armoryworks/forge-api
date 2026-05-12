namespace Forge.Core.Models;

/// <summary>
/// Pillar 3 — Body for upserting a tiered-price row on a VendorPart.
///
/// <para><strong>SCD Type 2 semantics</strong> (per
/// <c>docs/vendor-tier-pricing-history-2026-05-04.md</c>): if a
/// currently-effective tier exists at this <c>MinQuantity</c>, the handler
/// stamps its <c>EffectiveTo</c> to today and INSERTS a new row with the
/// supplied values — both rows are kept so PO line items pointing at the
/// superseded tier id remain traceable.</para>
///
/// <para><c>Currency</c> is intentionally NOT on this model — it lives on
/// the parent <c>VendorPart</c> now and is snapshotted into the tier row
/// at insert time. <c>EffectiveFrom</c> is optional; the handler defaults
/// it to <see cref="Forge.Core.Interfaces.IClock.UtcNow"/> when
/// omitted (matches the UI's "default to today" behavior).</para>
/// </summary>
public record UpsertVendorPartPriceTierRequestModel(
    decimal MinQuantity,
    decimal UnitPrice,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string? Notes);
