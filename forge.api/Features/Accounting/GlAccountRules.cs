using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>Shared chart-of-accounts rules + mapping used by the CoA management handlers.</summary>
public static class GlAccountRules
{
    /// <summary>The double-entry convention: assets and expenses carry a debit normal balance;
    /// liabilities, equity, and income carry a credit normal balance. Enforced on create/edit so
    /// statements and the posting engine stay coherent.</summary>
    public static NormalBalance ExpectedNormalBalance(AccountType type) => type switch
    {
        AccountType.Asset or AccountType.Expense => NormalBalance.Debit,
        _ => NormalBalance.Credit,
    };

    /// <summary>Projects an account to its management-screen model.</summary>
    public static GlAccountAdminModel ToAdminModel(this GlAccount a, bool hasPostings) => new(
        a.Id, a.AccountNumber, a.Name, a.AccountType.ToString(), a.NormalBalance.ToString(),
        a.ParentAccountId, a.IsPostable, a.IsControlAccount, a.IsActive, a.RequiresJob,
        a.RequiresCostCenter, a.CashFlowCategory?.ToString(), a.Description, hasPostings);
}
