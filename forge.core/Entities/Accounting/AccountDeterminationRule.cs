namespace Forge.Core.Entities.Accounting;

/// <summary>
/// Resolves a business-event <see cref="Key"/> to a GL account for a book —
/// business events never hardcode accounts (§5.1). Nullable scope columns are
/// added now even though only global rows seed, so Phase-2 scoping is config +
/// a resolver, not a migration. Precedence: most-specific scope wins.
/// <para>
/// Determination keys (seed superset now): AR_CONTROL, AP_CONTROL,
/// SALES_REVENUE, SALES_RETURNS, SALES_TAX_PAYABLE, DEFERRED_REVENUE,
/// CUSTOMER_DEPOSITS, INVENTORY_RAW, INVENTORY_WIP, INVENTORY_FG, COGS, GRNI,
/// PURCHASE_PRICE_VARIANCE, MATERIAL_USAGE_VARIANCE, INVENTORY_WRITEDOWN,
/// FREIGHT_CLEARING, CASH, RETAINED_EARNINGS, FX_GAIN, FX_LOSS, CTA, ROUNDING,
/// REFUNDS_PAYABLE, ACCRUED_EXPENSE, ACCRUED_WAGES, PREPAID_EXPENSE,
/// UNBILLED_REVENUE.
/// </para>
/// </summary>
public class AccountDeterminationRule : BaseEntity
{
    public int BookId { get; set; }

    public string Key { get; set; } = string.Empty;

    public int GlAccountId { get; set; }

    // Nullable scope columns — Phase-2 scoping is config, not a migration.
    public int? ItemId { get; set; }
    public int? CategoryId { get; set; }
    public int? ValuationClassId { get; set; }

    public Book Book { get; set; } = null!;
    public GlAccount GlAccount { get; set; } = null!;
}
