using FluentValidation;
using MediatR;

using Forge.Api.Features.Freight;
using Forge.Api.Validation;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

// SALES-LINE-CRUD: quote lines could only be set at create time. This adds an
// append path (gated to Draft), mirroring UpdateQuoteLine. A line may be a
// catalog part (PartId set) or a free-form line (PartId null) — same shape the
// create flow already accepts.
public record AddQuoteLineCommand(int QuoteId, CreateQuoteLineModel Data)
    : IRequest<QuoteDetailResponseModel>;

public class AddQuoteLineValidator : AbstractValidator<AddQuoteLineCommand>
{
    public AddQuoteLineValidator()
    {
        RuleFor(x => x.QuoteId).GreaterThan(0);
        RuleFor(x => x.Data.Description).NotEmpty().When(x => x.Data.LineType != DocumentLineType.Freight);
        RuleFor(x => x.Data.LineType).IsInEnum();
        RuleFor(x => x.Data.PartId).Null()
            .When(x => x.Data.LineType == DocumentLineType.Freight)
            .WithMessage("A freight line can't name a part.")
            .WithErrorCode(FreightLines.PartCode);
        RuleFor(x => x.Data.Quantity).GreaterThan(0).UnitScale();
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0).UnitScale();
    }
}

public class AddQuoteLineHandler(
    IQuoteRepository repo,
    IPartRepository partRepo,
    AppDbContext db,
    IMediator mediator,
    // AUDIT-19-S1: optional/null-default so isolated unit-test constructions stay valid; DI supplies it.
    Forge.Api.Services.CustomerPriceResolver? priceResolver = null)
    : IRequestHandler<AddQuoteLineCommand, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(AddQuoteLineCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindWithDetailsAsync(request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only draft quotes can have lines added.");

        if (request.Data.PartId is int partId && partId > 0)
        {
            var part = await partRepo.FindAsync(partId, cancellationToken);
            ActiveCheck.EnsureActive(part, "Part", "partId", partId);
        }

        var nextLineNumber = quote.Lines.Count == 0 ? 1 : quote.Lines.Max(l => l.LineNumber) + 1;

        // AUDIT-19-S1: when the caller didn't set a price (0) for a catalog part, resolve it from the
        // customer's price list so price lists are a live pricing input. An explicit price is kept.
        var unitPrice = request.Data.UnitPrice;
        if (unitPrice == 0m && priceResolver is not null && request.Data.PartId is int && request.Data.PartId.Value > 0)
            unitPrice = await priceResolver.ResolveUnitPriceAsync(quote.CustomerId, request.Data.PartId.Value, request.Data.Quantity, cancellationToken)
                        ?? unitPrice;

        var description = request.Data.LineType == DocumentLineType.Freight && string.IsNullOrWhiteSpace(request.Data.Description)
            ? FreightLines.DefaultDescription
            : request.Data.Description;
        quote.Lines.Add(new QuoteLine
        {
            PartId = request.Data.PartId,
            Description = description,
            LineType = request.Data.LineType,
            Quantity = request.Data.Quantity,
            UnitPrice = unitPrice,
            LineNumber = nextLineNumber,
            Notes = request.Data.Notes,
        });

        db.LogActivityAt(
            "line-added",
            $"Added quote line {nextLineNumber}: {description} ({request.Data.Quantity} × {unitPrice})",
            ("Quote", quote.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetQuoteByIdQuery(request.QuoteId), cancellationToken);
    }
}
