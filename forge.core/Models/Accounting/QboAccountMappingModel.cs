namespace Forge.Core.Models.Accounting;

/// <summary>
/// QB-001 mapping-editor row: a (postable, active) GL account joined with its
/// QuickBooks Online mapping when one exists (<see cref="QboAccountId"/> null =
/// unmapped — the push will refuse while this account has a nonzero net).
/// </summary>
public record QboAccountMappingModel(
    int GlAccountId,
    string AccountNumber,
    string AccountName,
    string? QboAccountId,
    string? QboAccountName);
