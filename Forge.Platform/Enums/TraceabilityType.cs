namespace Forge.Platform.Enums;

/// <summary>
/// Pillar 1 / Tier 0 — How units of a part are tracked through inventory
/// and production. Replaces the legacy <c>Part.IsSerialTracked</c> boolean,
/// which couldn't express lot-tracking (despite <c>LotRecord</c> existing).
///
/// • <c>None</c> — bulk-tracked only. Aggregate quantity, no per-unit identity.
/// • <c>Lot</c> — multiple units share a lot/batch number (food, chem,
///   pharma, anything with shelf-life). Recall scope is the lot.
/// • <c>Serial</c> — every unit has a unique serial number. Recall and
///   warranty scope is the unit.
///
/// Migration: <c>IsSerialTracked = true</c> → <c>Serial</c>; otherwise
/// <c>None</c>. Lot tracking opt-in per part after migration.
/// </summary>
public enum TraceabilityType
{
    None,
    Lot,
    Serial,
}
