using System.Text.RegularExpressions;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Features.Accounting;

/// <summary>Shared chart-of-accounts rules + mapping used by the CoA management handlers.</summary>
public static class GlAccountRules
{
    /// <summary>Account numbers start with a letter or digit and continue with letters, digits, dots
    /// or dashes (e.g. <c>60100</c>, <c>6010-02</c>, <c>1.100</c>).</summary>
    public static readonly Regex AccountNumberPattern = new(@"^[A-Za-z0-9][A-Za-z0-9.\-]*$", RegexOptions.Compiled);

    /// <summary>Projects an account to its management-screen model.</summary>
    public static GlAccountAdminModel ToAdminModel(this GlAccount a, bool hasPostings) => new(
        a.Id, a.AccountNumber, a.Name, a.AccountType.ToString(), a.NormalBalance.ToString(),
        a.ParentAccountId, a.IsPostable, a.IsControlAccount, a.IsActive, a.RequiresJob,
        a.RequiresCostCenter, a.CashFlowCategory?.ToString(), a.Description, hasPostings);
}
