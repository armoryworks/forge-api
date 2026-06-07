using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>One line of a journal template (a determination key OR an explicit account).</summary>
public sealed record JournalTemplateLineModel(
    string? AccountDeterminationKey,
    int? GlAccountId,
    decimal Debit,
    decimal Credit,
    string? Description,
    SubledgerPartyType? PartyType,
    int? PartyId);

/// <summary>Create a recurring/standard journal template.</summary>
public sealed record CreateJournalTemplateModel(
    int BookId,
    string Name,
    string? Description,
    string? Memo,
    bool AutoReverseNextPeriod,
    List<JournalTemplateLineModel> Lines);

/// <summary>A journal template + its lines.</summary>
public sealed record JournalTemplateModel(
    int Id,
    int BookId,
    string Name,
    string? Description,
    string? Memo,
    bool AutoReverseNextPeriod,
    bool IsActive,
    IReadOnlyList<JournalTemplateLineModel> Lines);

/// <summary>Result of posting an entry from a template.</summary>
public sealed record PostedFromTemplateModel(long JournalEntryId, long EntryNumber, DateOnly EntryDate);
