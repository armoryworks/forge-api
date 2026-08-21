namespace Forge.Core.Enums;

/// <summary>The basis on which a shared cost is allocated across cost centers.</summary>
public enum AllocationBasis
{
    /// <summary>Proportional to floor area (square footage).</summary>
    Sqft,
    /// <summary>Proportional to headcount.</summary>
    Headcount,
    /// <summary>Based on metered consumption.</summary>
    Metered,
    /// <summary>Charged directly to a single target.</summary>
    Direct,
    /// <summary>Split by explicit fixed proportions.</summary>
    FixedSplit
}
