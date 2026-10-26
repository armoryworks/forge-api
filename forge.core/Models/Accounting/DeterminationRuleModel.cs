namespace Forge.Core.Models.Accounting;

/// <summary>
/// One book-wide account-determination rule: the key a business event posts to, the GL account it resolves
/// to, and whether the Controller may re-point it (<see cref="Configurable"/>) and to which account type.
/// </summary>
public sealed record DeterminationRuleModel(
    string Key,
    int? GlAccountId,
    string? AccountNumber,
    string? AccountName,
    bool Configurable,
    string? RequiredAccountType);
