namespace Forge.Core.Models.Accounting;

/// <summary>
/// A part's standard unit cost decomposed into the three cost elements. Material + Labor + Overhead == the
/// blended standard unit cost used for inventory carrying, so any decomposition computed from these reconciles
/// to the standard value posted to inventory/WIP/FG.
/// </summary>
public sealed record StandardCostElements(decimal Material, decimal Labor, decimal Overhead)
{
    public decimal Total => Material + Labor + Overhead;

    public static readonly StandardCostElements Zero = new(0m, 0m, 0m);

    /// <summary>
    /// Reconciles a rolled-up decomposition to a manual standard override: when an override is set it is the
    /// carried total — labor + overhead come from the rollup and material is the reconciling residual (so the
    /// elements sum to the override); if conversion alone exceeds the override it is scaled to fit with no
    /// material. No override → the rollup is returned unchanged. Shared by the resolver (live) and the
    /// part-standard recalc (persisted snapshot) so both produce the same decomposition.
    /// </summary>
    public static StandardCostElements ReconcileToOverride(StandardCostElements rollup, decimal? overrideTotal)
    {
        if (overrideTotal is decimal total && total > 0m)
        {
            var conversion = rollup.Labor + rollup.Overhead;
            if (conversion > total)
            {
                var labor = decimal.Round(total * (conversion == 0m ? 0m : rollup.Labor / conversion), 2);
                return new StandardCostElements(0m, labor, total - labor);
            }
            return new StandardCostElements(total - conversion, rollup.Labor, rollup.Overhead);
        }
        return rollup;
    }
}
