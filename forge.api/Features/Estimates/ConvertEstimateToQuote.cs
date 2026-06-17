using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Estimates;

public record ConvertEstimateToQuoteCommand(int EstimateId) : IRequest<QuoteListItemModel>;

public class ConvertEstimateToQuoteHandler(AppDbContext db, IQuoteRepository quoteRepo)
    : IRequestHandler<ConvertEstimateToQuoteCommand, QuoteListItemModel>
{
    public async Task<QuoteListItemModel> Handle(ConvertEstimateToQuoteCommand request, CancellationToken ct)
    {
        var estimate = await db.Quotes
            .Include(e => e.Customer)
            .Include(e => e.GeneratedQuote)
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == request.EstimateId && e.Type == QuoteType.Estimate && e.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Estimate {request.EstimateId} not found.");

        if (estimate.GeneratedQuote != null)
            throw new InvalidOperationException("Estimate has already been converted to a quote.");

        var quoteNumber = await quoteRepo.GenerateNextQuoteNumberAsync(ct);
        var quote = new Quote
        {
            Type = QuoteType.Quote,
            QuoteNumber = quoteNumber,
            CustomerId = estimate.CustomerId,
            Status = QuoteStatus.Draft,
            Notes = estimate.Description ?? estimate.Notes,
            ExpirationDate = estimate.ExpirationDate,
            TaxRate = 0,
            SourceEstimateId = estimate.Id,
        };

        // #24 / BE-3: carry the estimate's line items into the new quote. The header-only
        // convert dropped them entirely. Lump-sum lines (PartId == null) copy as-is — they
        // remain editable/replaceable in the quote editor; an interactive per-line "eliminate
        // or pick a real part" prompt at convert time is a tracked UX follow-up.
        foreach (var line in estimate.Lines.OrderBy(l => l.LineNumber))
        {
            quote.Lines.Add(new QuoteLine
            {
                PartId = line.PartId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineNumber = line.LineNumber,
                Notes = line.Notes,
            });
        }

        // BE-3: an un-itemized estimate (just an EstimatedAmount, no lines) must still become
        // a quote with at least one line — otherwise the quote converts empty and can't be
        // sent/ordered. Synthesize a single lump-sum line (PartId null) the user can later
        // itemize or replace with real parts.
        if (quote.Lines.Count == 0 && estimate.EstimatedAmount is decimal estAmount && estAmount > 0)
        {
            quote.Lines.Add(new QuoteLine
            {
                PartId = null,
                Description = string.IsNullOrWhiteSpace(estimate.Title) ? "Estimated amount" : estimate.Title!,
                Quantity = 1m,
                UnitPrice = estAmount,
                LineNumber = 1,
            });
        }

        db.Quotes.Add(quote);
        estimate.Status = QuoteStatus.ConvertedToQuote;
        estimate.ConvertedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var total = quote.Lines.Sum(l => l.Quantity * l.UnitPrice);

        return new QuoteListItemModel(
            quote.Id,
            quote.QuoteNumber!,
            estimate.CustomerId,
            estimate.Customer.Name,
            quote.Status.ToString(),
            quote.Lines.Count,
            total,
            quote.ExpirationDate,
            quote.CreatedAt);
    }
}
