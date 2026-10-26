namespace Forge.Core.Enums;

/// <summary>
/// Who pays the carrier, and whether the seller bills the freight on to the customer. Stored as an integer.
/// </summary>
public enum FreightTerms
{
    /// <summary>The seller pays the carrier and absorbs the cost.</summary>
    Prepaid = 0,

    /// <summary>The seller pays the carrier and bills the freight to the customer.</summary>
    PrepaidAndAdd = 1,

    /// <summary>The customer pays the carrier on delivery.</summary>
    Collect = 2,

    /// <summary>A third party pays the carrier.</summary>
    ThirdParty = 3,
}
