namespace Forge.Core.Models.Accounting;

/// <summary>
/// One jurisdiction's row of the sales-tax liability report: the seller
/// sales-tax the business owes for invoices shipping to a single jurisdiction
/// (US state) over the report window.
/// </summary>
public sealed record SalesTaxLiabilityJurisdictionRow
{
    /// <summary>
    /// The taxing jurisdiction — the 2-letter ship-to state code of the invoices.
    /// Null when no ship-to state is on record (no order ship-to and no customer
    /// shipping address); such invoices are grouped into a single "unassigned"
    /// row rather than dropped.
    /// </summary>
    public string? Jurisdiction { get; init; }

    /// <summary>
    /// The taxable base for the seller's liability — Σ taxable invoice subtotal
    /// of the <b>seller-collected</b> invoices in this jurisdiction (the base the
    /// liability is computed on). Marketplace-facilitator invoices are excluded:
    /// the platform, not the seller, owes that tax.
    /// </summary>
    public decimal TaxableBase { get; init; }

    /// <summary>
    /// The seller sales-tax liability for this jurisdiction — Σ invoice
    /// <see cref="Forge.Core.Entities.Invoice.TaxAmount"/> in functional currency.
    /// Marketplace-collected tax contributes zero.
    /// </summary>
    public decimal Liability { get; init; }

    /// <summary>Count of invoices shipping to this jurisdiction in the window (all statuses except Draft/Voided).</summary>
    public int InvoiceCount { get; init; }
}
