namespace Forge.Core.Models.Accounting;

/// <summary>
/// One jurisdiction's row of the sales-tax liability report: the seller
/// sales-tax the business owes for orders shipping to a single jurisdiction
/// (US state) over the report window.
/// </summary>
public sealed record SalesTaxLiabilityJurisdictionRow
{
    /// <summary>
    /// The taxing jurisdiction — the 2-letter ship-to state code of the orders.
    /// Null when no ship-to state is on record for the order (neither a
    /// customer shipping address nor a frozen retail ship-to); such orders are
    /// grouped into a single "unassigned" row rather than dropped.
    /// </summary>
    public string? Jurisdiction { get; init; }

    /// <summary>
    /// The taxable base for the seller's liability — Σ order subtotal of the
    /// <b>seller-collected</b> orders in this jurisdiction (the base the
    /// liability is computed on). Marketplace-facilitator orders are excluded:
    /// the platform, not the seller, owes that tax.
    /// </summary>
    public decimal TaxableBase { get; init; }

    /// <summary>
    /// The seller sales-tax liability for this jurisdiction — Σ
    /// <see cref="Forge.Core.Entities.SalesOrder.SellerTaxLiability"/>, never
    /// <c>TaxAmount</c>. Marketplace-collected tax contributes zero.
    /// </summary>
    public decimal Liability { get; init; }

    /// <summary>Count of orders shipping to this jurisdiction in the window (all statuses except Draft/Cancelled).</summary>
    public int OrderCount { get; init; }
}
