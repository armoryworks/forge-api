namespace Forge.Core.Models.Accounting;

/// <summary>
/// Sales-tax liability report — what sales tax the business owes a taxing
/// authority, aggregated by jurisdiction (ship-to US state) over an optional
/// date range, in functional currency. With the built-in GL on it reads the tax
/// each invoice's AR entry posted to Sales Tax Payable (by entry date, void
/// reversals included), so it ties to that account; otherwise it is built from
/// finalized <see cref="Forge.Core.Entities.Invoice"/>s by invoice date.
///
/// <para><b>Seller liability, not collected tax.</b> Under marketplace-facilitator
/// law the platform collects and remits tax on the orders it brokers; that money
/// rides the document because the buyer paid it but is never the seller's
/// payable, so invoices for those orders contribute zero here.</para>
/// </summary>
public sealed record SalesTaxLiabilityReport
{
    /// <summary>Inclusive start of the reporting window (null = unbounded).</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive end of the reporting window (null = unbounded).</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>One row per jurisdiction with a non-zero invoice count, ordered by liability descending.</summary>
    public IReadOnlyList<SalesTaxLiabilityJurisdictionRow> Jurisdictions { get; init; } = [];

    /// <summary>Grand total taxable base across all jurisdictions (seller-collected invoices only).</summary>
    public decimal TotalTaxableBase { get; init; }

    /// <summary>Grand total seller sales-tax liability across all jurisdictions.</summary>
    public decimal TotalLiability { get; init; }

    /// <summary>Grand total invoice count across all jurisdictions.</summary>
    public int TotalInvoiceCount { get; init; }

    /// <summary>
    /// Tax on marketplace-facilitator invoices in the window (functional currency), left out of
    /// <see cref="TotalLiability"/>. With the built-in GL on it is what those invoices posted to Sales Tax Payable,
    /// so <see cref="TotalLiability"/> plus this ties to the invoice and credit memo postings to that account; the
    /// marketplace's settlement takes it back out of the account when it withholds the tax.
    /// </summary>
    public decimal MarketplaceCollectedTax { get; init; }
}
