namespace Forge.Core.Enums.Accounting;

/// <summary>
/// The five fundamental GL account classifications (US-GAAP). Drives statement
/// placement (Asset/Liability/Equity → Balance Sheet; Income/Expense → P&amp;L)
/// and the default <see cref="NormalBalance"/>.
/// </summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}
