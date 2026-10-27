namespace Forge.Core.Models.Accounting;

/// <summary>
/// The check of one opening open item: the resolved party (when found) and the problem codes that would stop
/// it posting (empty when the row is good).
/// </summary>
public record OpeningOpenItemRowResultModel(
    int RowNumber,
    int? PartyId,
    string? PartyName,
    IReadOnlyList<string> Errors);
