namespace Forge.Platform.Enums;

/// <summary>
/// Per-part override of the global backflush policy. Controls how component
/// consumption is recorded against parent assemblies during production.
///
/// See <c>phase-4-output/part-type-field-relevance.md</c> § 8 (Tier 3).
/// </summary>
public enum BackflushPolicy
{
    /// <summary>
    /// Components are auto-consumed when the parent operation is reported
    /// complete (back-flushed against the BOM).
    /// </summary>
    Auto,

    /// <summary>
    /// Components must be issued manually (operator pick-and-issue at the
    /// work center). Default for high-value or serialized parts.
    /// </summary>
    Manual,

    /// <summary>
    /// No automatic consumption — the part is tracked elsewhere (e.g., a
    /// phantom that explodes at MRP and is never inventoried at this level).
    /// </summary>
    None
}
