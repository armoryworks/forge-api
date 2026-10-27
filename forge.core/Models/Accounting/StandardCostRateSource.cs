namespace Forge.Core.Models.Accounting;

/// <summary>Where a standard cost roll took its work-center rates from.</summary>
public static class StandardCostRateSource
{
    /// <summary>The work centers' current labor and burden rates (with the Tier-2 departmental profile when on).</summary>
    public const string WorkCenter = "work-center";

    /// <summary>The Tier-3 work-center rates frozen for the costing period that covers the roll date.</summary>
    public const string FrozenPeriod = "frozen-period";
}
