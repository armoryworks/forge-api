namespace Forge.Core.Enums;

/// <summary>Classifies a cost center by the kind of work it performs and how its costs are treated.</summary>
public enum CostCenterType
{
    /// <summary>Directly produces inventoriable output.</summary>
    Production,
    /// <summary>Supports production but does not itself produce output.</summary>
    Support,
    /// <summary>Selling, general and administrative — period cost, never inventoried.</summary>
    Sga,
    /// <summary>Storage and material handling.</summary>
    Warehouse
}
