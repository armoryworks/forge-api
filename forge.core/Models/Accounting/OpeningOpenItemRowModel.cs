using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// One opening receivable (customer) or payable (vendor) open item as keyed or imported for the §7A
/// conversion. The party is given by id, or by <see cref="PartyRef"/> (customer or vendor number, or name)
/// for a CSV row; <see cref="Amount"/> is the open balance in the book's functional currency.
/// </summary>
public record OpeningOpenItemRowModel(
    SubledgerPartyType PartyType,
    int? PartyId,
    string? PartyRef,
    string? DocumentNumber,
    DateOnly? DocumentDate,
    DateOnly? DueDate,
    decimal Amount,
    string? Description);
