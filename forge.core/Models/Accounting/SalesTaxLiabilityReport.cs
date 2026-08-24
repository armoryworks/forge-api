namespace Forge.Core.Models.Accounting;

/// <summary>
/// Sales-tax liability report — what sales tax the business owes a taxing
/// authority, aggregated by jurisdiction (ship-to US state) over an optional
/// date range. Read-only aggregate over <see cref="Forge.Core.Entities.SalesOrder"/>.
///
/// <para><b>Seller liability, not collected tax.</b> Every amount is derived
/// from <see cref="Forge.Core.Entities.SalesOrder.SellerTaxLiability"/> — the
/// portion the install actually owes — never <c>TaxAmount</c>. Under
/// marketplace-facilitator law the platform collects and remits tax on the
/// orders it brokers; that money rides the document because the buyer paid it
/// but is never the seller's payable, so those orders contribute zero here.</para>
/// </summary>
public sealed record SalesTaxLiabilityReport
{
    /// <summary>Inclusive start of the reporting window (null = unbounded).</summary>
    public DateOnly? FromDate { get; init; }

    /// <summary>Inclusive end of the reporting window (null = unbounded).</summary>
    public DateOnly? ToDate { get; init; }

    /// <summary>One row per jurisdiction with a non-zero order count, ordered by liability descending.</summary>
    public IReadOnlyList<SalesTaxLiabilityJurisdictionRow> Jurisdictions { get; init; } = [];

    /// <summary>Grand total taxable base across all jurisdictions (seller-collected orders only).</summary>
    public decimal TotalTaxableBase { get; init; }

    /// <summary>Grand total seller sales-tax liability across all jurisdictions.</summary>
    public decimal TotalLiability { get; init; }

    /// <summary>Grand total order count across all jurisdictions.</summary>
    public int TotalOrderCount { get; init; }
}
