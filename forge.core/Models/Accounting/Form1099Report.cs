namespace Forge.Core.Models.Accounting;

/// <summary>
/// Annual 1099 report — every vendor flagged as a 1099 payee
/// (<see cref="Forge.Core.Entities.Vendor.Is1099"/>) with the total cash paid to
/// it in the calendar year, so the office can prepare Form 1099-NEC filings.
/// Read-only aggregate over <see cref="Forge.Core.Entities.VendorPayment"/>; no
/// mutation, so no <c>ActivityLog</c>.
///
/// <para><b>Threshold.</b> <see cref="Threshold"/> is the IRS 1099-NEC reporting
/// floor for the year ($600 through tax year 2025, $2,000 from tax year 2026).
/// Every flagged vendor paid anything in the year is listed so the office can see
/// who was paid but under-threshold; <see cref="Form1099VendorRow.MeetsThreshold"/>
/// marks the ones that actually require a filing. Flagged vendors paid nothing are
/// left out unless asked for and counted in <see cref="ZeroPaymentVendorCount"/>.</para>
/// </summary>
public sealed record Form1099Report
{
    /// <summary>The calendar year the payments were made in.</summary>
    public int Year { get; init; }

    /// <summary>IRS 1099-NEC reporting threshold for <see cref="Year"/>, applied to each vendor's total.</summary>
    public decimal Threshold { get; init; }

    /// <summary>One row per 1099-flagged vendor, ordered by total payments descending.</summary>
    public IReadOnlyList<Form1099VendorRow> Vendors { get; init; } = [];

    /// <summary>Grand total paid to all 1099-flagged vendors in the year.</summary>
    public decimal TotalPayments { get; init; }

    /// <summary>Count of vendors whose year total reaches the threshold (i.e. require a 1099 filing).</summary>
    public int ReportableVendorCount { get; init; }

    /// <summary>Count of 1099-flagged vendors paid nothing in the year, whether or not they are listed.</summary>
    public int ZeroPaymentVendorCount { get; init; }
}
