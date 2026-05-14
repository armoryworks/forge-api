using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
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

        db.Quotes.Add(quote);
        estimate.Status = QuoteStatus.ConvertedToQuote;
        estimate.ConvertedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new QuoteListItemModel(
            quote.Id,
            quote.QuoteNumber!,
            estimate.CustomerId,
            estimate.Customer.Name,
            quote.Status.ToString(),
            0,
            0m,
            quote.ExpirationDate,
            quote.CreatedAt);
    }
}
