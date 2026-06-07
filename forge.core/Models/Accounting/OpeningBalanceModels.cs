using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// One line of the opening-balance journal (§7A conversion). A balance-sheet account's opening balance, or an
/// AR/AP open item (control account + party). Uses a determination key or an explicit account.
/// </summary>
public sealed record OpeningBalanceLineModel(
    string? AccountDeterminationKey,
    int? GlAccountId,
    decimal Debit,
    decimal Credit,
    SubledgerPartyType? PartyType,
    int? PartyId,
    string? Description);

/// <summary>The conversion opening-balance journal for a book as of the go-live cutover date.</summary>
public sealed record PostOpeningBalancesModel(
    int BookId,
    DateOnly AsOfDate,
    List<OpeningBalanceLineModel> Lines);

/// <summary>Result of posting the opening-balance journal.</summary>
public sealed record OpeningBalanceResult(long JournalEntryId, long EntryNumber, decimal TotalDebit);
