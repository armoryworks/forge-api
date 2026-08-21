namespace Forge.Core.Enums;

/// <summary>How a BOM component participates in inventory and cost rollup.</summary>
public enum BomComponentType
{
    /// <summary>A stocked item that carries inventory value.</summary>
    Stocked,
    /// <summary>A non-stocked pass-through whose cost lands at the parent level.</summary>
    Phantom,
    /// <summary>Expensed on consumption; rolls into overhead rather than material inventory.</summary>
    Expensed
}
