using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Quotes;

// BE-1 / Q-3: quote lines were immutable. This adds a line-edit path, gated to Draft
// (originals are preserved in the history/audit trail — lossless system).
public record UpdateQuoteLineCommand(int QuoteId, int LineId, UpdateOrderLineRequestModel Data)
    : IRequest<QuoteDetailResponseModel>;

public class UpdateQuoteLineValidator : AbstractValidator<UpdateQuoteLineCommand>
{
    public UpdateQuoteLineValidator()
    {
        RuleFor(x => x.QuoteId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.UnitPrice).GreaterThanOrEqualTo(0);
    }
}

public class UpdateQuoteLineHandler(IQuoteRepository repo, IMediator mediator)
    : IRequestHandler<UpdateQuoteLineCommand, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(UpdateQuoteLineCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindWithDetailsAsync(request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only draft quotes can have their lines edited.");

        var line = quote.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Quote line {request.LineId} not found");

        line.Description = request.Data.Description;
        line.Quantity = request.Data.Quantity;
        line.UnitPrice = request.Data.UnitPrice;
        line.Notes = request.Data.Notes;

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetQuoteByIdQuery(request.QuoteId), cancellationToken);
    }
}
