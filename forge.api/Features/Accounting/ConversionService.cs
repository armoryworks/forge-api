using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class ConversionService(AppDbContext db, IPostingEngine postingEngine) : IConversionService
{
    public async Task<OpeningBalanceResult> PostOpeningBalancesAsync(
        PostOpeningBalancesModel model, int postedByUserId, CancellationToken ct = default)
    {
        if (model.Lines is not { Count: > 0 })
            throw new InvalidOperationException("The opening-balance journal needs at least one line.");

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == model.BookId).Select(b => (int?)b.FunctionalCurrencyId).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {model.BookId} not found for conversion.");

        var request = new PostingRequest
        {
            BookId = model.BookId,
            EntryDate = model.AsOfDate,
            Source = JournalSource.Conversion,
            SourceType = "OpeningBalance",
            SourceId = model.BookId,
            CurrencyId = currencyId,
            Memo = $"Opening balances (conversion) as of {model.AsOfDate:yyyy-MM-dd}",
            // One opening journal per book — a re-run returns the existing entry (no double-load).
            IdempotencyKey = $"{JournalSource.Conversion}:OpeningBalance:{model.BookId}",
            // The opening period may already be soft-closed at cutover; the opening post is audited.
            AllowSoftClosedOverride = true,
            Lines = model.Lines.Select(l => new PostingLine
            {
                AccountKey = l.AccountDeterminationKey,
                GlAccountId = l.GlAccountId,
                Debit = l.Debit,
                Credit = l.Credit,
                PartyType = l.PartyType,
                PartyId = l.PartyId,
                Description = l.Description ?? "Opening balance",
            }).ToList(),
        };

        // The engine enforces balance (UNBALANCED) and the control-line party requirement (so AR/AP open
        // items must name a customer/vendor) — opening balances can't post a lopsided or party-less control.
        var entry = await postingEngine.PostAsync(request, postedByUserId, ct);
        return new OpeningBalanceResult(entry.Id, entry.EntryNumber, entry.Lines.Sum(l => l.Debit));
    }
}
