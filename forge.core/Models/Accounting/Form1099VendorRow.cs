namespace Forge.Core.Models.Accounting;

/// <summary>
/// One 1099-flagged vendor's line on the annual 1099 report: the total cash paid
/// to the vendor in the calendar year and whether it reaches the IRS reporting
/// threshold. <see cref="TaxId"/> is carried in full — the UI masks all but the
/// last four digits for display.
/// </summary>
public sealed record Form1099VendorRow
{
    public int VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string? VendorNumber { get; init; }

    /// <summary>Vendor TIN/EIN/SSN in full; masked in the UI.</summary>
    public string? TaxId { get; init; }

    /// <summary>Σ <see cref="Forge.Core.Entities.VendorPayment.Amount"/> paid to the vendor in the year.</summary>
    public decimal TotalPayments { get; init; }

    /// <summary>True when <see cref="TotalPayments"/> reaches the reportable threshold (1099-NEC = $600).</summary>
    public bool MeetsThreshold { get; init; }
}
