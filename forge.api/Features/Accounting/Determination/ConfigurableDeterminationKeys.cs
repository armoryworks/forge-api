using Forge.Core.Enums.Accounting;

namespace Forge.Api.Features.Accounting.Determination;

/// <summary>
/// The determination keys a Controller may re-point to another account of the book, with the account type
/// each needs. Every other key (control accounts, clearing accounts, GRNI, tax) is system-managed.
/// </summary>
public static class ConfigurableDeterminationKeys
{
    public static readonly IReadOnlyDictionary<string, AccountType> RequiredType = new Dictionary<string, AccountType>(StringComparer.Ordinal)
    {
        ["FREIGHT_INCOME"] = AccountType.Income,
        ["SALES_DISCOUNTS"] = AccountType.Income,
        ["FREIGHT_IN"] = AccountType.Expense,
        ["FREIGHT_OUT"] = AccountType.Expense,
        ["PURCHASE_DISCOUNTS"] = AccountType.Expense,
        ["BAD_DEBT_EXPENSE"] = AccountType.Expense,
        ["OPENING_BALANCE_EQUITY"] = AccountType.Equity,
    };
}
