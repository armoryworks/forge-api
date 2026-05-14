namespace Forge.Platform.Enums;

/// <summary>
/// Pillar 1 — How a part is sourced. One of three orthogonal axes (with
/// <see cref="InventoryClass"/> and ItemKind) that replaced the legacy
/// overloaded single-axis <c>PartType</c> enum (retired pre-beta).
///
/// • <c>Make</c> — produced in-house from a routing + BOM (or single-step
///   from raw stock). The shop owns the production.
/// • <c>Buy</c> — purchased from a vendor as a finished item; no in-house
///   manufacturing operations.
/// • <c>Subcontract</c> — the entire part is built by a vendor on our
///   behalf (we never touch it). Distinct from Make-with-IsSubcontract-op,
///   which means "we make most of it, send out for one step."
/// • <c>Phantom</c> — logical grouping construct, never stocked. BOM
///   explodes through to children; no physical inventory of the parent.
///
/// See <c>phase-4-output/part-type-field-relevance.md</c> § 1 for the
/// full rationale.
/// </summary>
public enum ProcurementSource
{
    Make,
    Buy,
    Subcontract,
    Phantom,
}
