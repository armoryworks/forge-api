namespace Forge.Core.Models.Accounting;

/// <summary>A chart-of-accounts row for the management screen — the full editable surface plus
/// <c>HasPostings</c>, which locks the structural fields (number / type / normal balance) in the editor
/// once the account carries journal activity (changing them would corrupt statements + determination).</summary>
public record GlAccountAdminModel(
    int Id,
    string AccountNumber,
    string Name,
    string AccountType,
    string NormalBalance,
    int? ParentAccountId,
    bool IsPostable,
    bool IsControlAccount,
    bool IsActive,
    bool RequiresJob,
    bool RequiresCostCenter,
    string? CashFlowCategory,
    string? Description,
    bool HasPostings);
