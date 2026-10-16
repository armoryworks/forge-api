namespace Forge.Api.Features.Accounting;

/// <summary>
/// §7A cutover state for one book: whether its Conversion opening journal is posted and whether
/// CAP-ACCT-FULLGL is already on — what the opening-journal screen shows before and after posting.
/// </summary>
public record ConversionStatusModel(
    int BookId,
    string BookName,
    int FunctionalCurrencyId,
    bool OpeningJournalPosted,
    long? OpeningEntryId,
    long? OpeningEntryNumber,
    DateOnly? OpeningEntryDate,
    bool FullGlEnabled);
