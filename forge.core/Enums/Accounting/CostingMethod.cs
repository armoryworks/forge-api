namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Inventory costing method, set per book (<c>Book.DefaultCostingMethod</c>) and
/// overridable per part (<c>Part.ValuationClassId</c>). Product default is
/// <see cref="Standard"/> (manufacturing target); see ACCOUNTING_SUITE_PLAN §8.1.
/// LIFO and Specific-ID are intentionally out of scope for v1.
/// <para>Not consumed by the engine until Phase 2 — present now so the method is
/// configuration, not a later migration.</para>
/// </summary>
public enum CostingMethod
{
    /// <summary>Planned cost per part; differences post to variance accounts.</summary>
    Standard,

    /// <summary>Running moving-average unit cost, recomputed on each receipt.</summary>
    WeightedAverage,

    /// <summary>First-in, first-out cost layers consumed oldest-first.</summary>
    Fifo,
}
