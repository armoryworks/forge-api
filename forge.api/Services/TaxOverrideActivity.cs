using Forge.Core.Enums;

namespace Forge.Api.Services;

/// <summary>Activity and document text for an accepted tax-rate override.</summary>
public static class TaxOverrideActivity
{
    /// <summary>The reason in words, e.g. "Out of jurisdiction".</summary>
    public static string ReasonLabel(TaxOverrideReason reason) => reason switch
    {
        TaxOverrideReason.Resale => "Resale",
        TaxOverrideReason.ExemptOrganization => "Exempt organization",
        TaxOverrideReason.Government => "Government",
        TaxOverrideReason.OutOfJurisdiction => "Out of jurisdiction",
        _ => "Other",
    };

    /// <summary>The activity row for an override, e.g. "Tax rate set to 0% (Resale: certificate R-1)".</summary>
    public static string Describe(decimal rate, TaxOverrideStamp stamp)
        => $"Tax rate set to {MoneyFormat.Percent(rate)} ({ReasonLabel(stamp.Reason)}{(stamp.Note is null ? string.Empty : $": {stamp.Note}")})";
}
