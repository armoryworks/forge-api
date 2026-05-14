namespace Forge.Platform.Enums;

/// <summary>
/// Pillar 1 — What inventory bucket a part lives in. One of three orthogonal
/// axes (with <see cref="ProcurementSource"/> and ItemKind) that replaced the
/// legacy overloaded single-axis <c>PartType</c> enum (retired pre-beta).
///
/// • <c>Raw</c> — bulk input materials (bar stock, sheet, pellets, resin,
///   wire spool). Issued to production; not sold.
/// • <c>Component</c> — single-piece part consumed on a parent BOM. May be
///   purchased (COTS bracket, fastener, IC) or made in-house (machined
///   part with no sub-BOM beyond raw issuance).
/// • <c>Subassembly</c> — multi-level build that's used in higher
///   assemblies. Has its own BOM and routing.
/// • <c>FinishedGood</c> — shippable end product sold to customers.
/// • <c>Consumable</c> — issued to overhead, not to a parent BOM (cutting
///   fluid, gloves, sandpaper). Single-use semantics.
/// • <c>Tool</c> — durable item used many times until worn (drill bits,
///   gauges, end mills). High-value durables also link to <c>Asset</c>.
///
/// See <c>phase-4-output/part-type-field-relevance.md</c> § 1 for the
/// full rationale.
/// </summary>
public enum InventoryClass
{
    Raw,
    Component,
    Subassembly,
    FinishedGood,
    Consumable,
    Tool,
}
