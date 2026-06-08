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
}
