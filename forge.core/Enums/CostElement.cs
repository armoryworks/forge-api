namespace Forge.Core.Enums;

/// <summary>The buckets a standard cost is decomposed into for reporting and variance analysis.</summary>
public enum CostElement
{
    /// <summary>Material.</summary>
    Mat,
    /// <summary>Material overhead.</summary>
    Moh,
    /// <summary>Direct labor.</summary>
    Lab,
    /// <summary>Labor overhead.</summary>
    Loh,
    /// <summary>Machine cost.</summary>
    Mch,
    /// <summary>Variable machine overhead.</summary>
    Mohv,
    /// <summary>Fixed machine overhead.</summary>
    Mohf,
    /// <summary>Subcontract.</summary>
    Sub
}
