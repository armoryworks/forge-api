using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;

namespace Forge.Api.Features.Accounting;

/// <inheritdoc />
public sealed class ConversionService(
    AppDbContext db, IPostingEngine postingEngine, IBusinessIdentifierService identifiers) : IConversionService
{
    public async Task<OpeningBalanceResult> PostOpeningBalancesAsync(
        PostOpeningBalancesModel model, int postedByUserId, CancellationToken ct = default)
    {
        if (model.Lines is not { Count: > 0 })
            throw new InvalidOperationException("The opening-balance journal needs at least one line.");

        var currencyId = await db.Books.AsNoTracking()
            .Where(b => b.Id == model.BookId).Select(b => (int?)b.FunctionalCurrencyId).FirstOrDefaultAsync(ct)
            ?? throw new PostingException("NO_POSTING_BOOK", $"Book {model.BookId} not found for conversion.");

        var idempotencyKey = $"{JournalSource.Conversion}:OpeningBalance:{model.BookId}";
        var alreadyPosted = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(e => e.BookId == model.BookId && e.IdempotencyKey == idempotencyKey, ct);
        if (!alreadyPosted)
        {
            await OpeningInventoryGuard.EnsureNoInventoryLinesAsync(
                db,
                model.BookId,
                model.Lines.Where(l => l.GlAccountId is not null).Select(l => l.GlAccountId!.Value),
                model.Lines.Where(l => l.GlAccountId is null && l.AccountDeterminationKey is not null).Select(l => l.AccountDeterminationKey!),
                ct);
            await ConversionOpenItems.ValidateDocumentNumbersAsync(db, model.Lines, ct);
        }

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
            IdempotencyKey = idempotencyKey,
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

        var details = model.Lines.Select((l, i) => (LineNumber: i + 1, Line: l))
            .ToDictionary(x => x.LineNumber, x => x.Line);
        await ConversionOpenItems.EnsureForEntryAsync(db, identifiers, entry.Id, alreadyPosted ? null : details, ct);

        return new OpeningBalanceResult(entry.Id, entry.EntryNumber, entry.Lines.Sum(l => l.Debit));
    }
}
