using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Estimates;

public record ConvertEstimateToQuoteCommand(
    int EstimateId,
    IReadOnlyList<EstimateLineResolutionModel>? LineResolutions = null)
    : IRequest<QuoteListItemModel>;

public class ConvertEstimateToQuoteValidator : AbstractValidator<ConvertEstimateToQuoteCommand>
{
    public ConvertEstimateToQuoteValidator()
    {
        RuleFor(x => x.EstimateId).GreaterThan(0);
        RuleForEach(x => x.LineResolutions).ChildRules(resolution =>
        {
            resolution.RuleFor(r => r.EstimateLineId).GreaterThan(0);
            resolution.RuleFor(r => r.PartId)
                .NotNull()
                .When(r => r.Action == EstimateLineResolutionAction.ReplaceWithPart)
                .WithMessage("A part is required when replacing a lump-sum line.");
            resolution.RuleFor(r => r.PartId!.Value)
                .GreaterThan(0)
                .When(r => r.PartId.HasValue);
            resolution.RuleFor(r => r.UnitPrice!.Value)
                .GreaterThanOrEqualTo(0)
                .When(r => r.UnitPrice.HasValue);
        });
    }
}

public class ConvertEstimateToQuoteHandler(
    AppDbContext db,
    IQuoteRepository quoteRepo,
    IPartRepository partRepo,
    // AUDIT-19-S1 pattern: optional/null-default so isolated unit-test constructions stay valid; DI supplies it.
    Forge.Api.Services.CustomerPriceResolver? priceResolver = null)
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

        var resolutions = (request.LineResolutions ?? []).ToDictionary(r => r.EstimateLineId);

        // Every resolution must target a line that actually belongs to this estimate.
        var lineIds = estimate.Lines.Select(l => l.Id).ToHashSet();
        var orphan = resolutions.Keys.FirstOrDefault(id => !lineIds.Contains(id), -1);
        if (orphan != -1)
            throw new KeyNotFoundException($"Estimate line {orphan} not found on estimate {estimate.Id}.");

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

        // #24 / BE-3: carry the estimate's line items into the new quote, applying the caller's
        // per-line resolutions. Lump-sum lines (PartId == null) are prompted about at convert
        // time in the UI — each is either eliminated (skipped) or replaced with a real catalog
        // part. Lines without a resolution (or resolved Keep) copy as-is, which also preserves
        // the legacy no-resolutions behavior for backward compatibility.
        var eliminated = 0;
        var replaced = 0;
        var lineNumber = 1;
        foreach (var line in estimate.Lines.OrderBy(l => l.LineNumber))
        {
            resolutions.TryGetValue(line.Id, out var resolution);

            if (resolution?.Action == EstimateLineResolutionAction.Eliminate)
            {
                eliminated++;
                continue;
            }

            var partId = line.PartId;
            var unitPrice = line.UnitPrice;
            if (resolution?.Action == EstimateLineResolutionAction.ReplaceWithPart)
            {
                // Validator guarantees PartId is present; still verify it references a live part.
                var replacementPartId = resolution.PartId!.Value;
                var part = await partRepo.FindAsync(replacementPartId, ct);
                ActiveCheck.EnsureActive(part, "Part", "lineResolutions.partId", replacementPartId);

                partId = replacementPartId;
                // Caller-supplied price wins; otherwise prefer the customer's resolved
                // price-list price (AUDIT-19-S1), falling back to the line's amount.
                unitPrice = resolution.UnitPrice
                    ?? (priceResolver is not null
                        ? await priceResolver.ResolveUnitPriceAsync(estimate.CustomerId, replacementPartId, ct) ?? line.UnitPrice
                        : line.UnitPrice);
                replaced++;
            }

            quote.Lines.Add(new QuoteLine
            {
                PartId = partId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                LineNumber = lineNumber++,
                Notes = line.Notes,
            });
        }

        // Eliminating every line would produce an empty quote that can't be sent/ordered.
        if (estimate.Lines.Count > 0 && quote.Lines.Count == 0)
            throw new InvalidOperationException("Cannot eliminate every line — the quote must keep at least one line.");

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

        // Audit trail on both transactional entities (quote.Id is assigned by the save above).
        var resolutionSummary = eliminated + replaced > 0
            ? $" ({quote.Lines.Count} lines carried, {eliminated} eliminated, {replaced} replaced with parts)"
            : string.Empty;
        db.LogActivityAt(
            "converted-to-quote",
            $"Converted estimate to quote {quoteNumber}{resolutionSummary}",
            ("Quote", estimate.Id), ("Quote", quote.Id));
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
