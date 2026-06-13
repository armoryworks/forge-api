using FluentValidation;
using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Quotes;

// SALES-LINE-CRUD: remove a single line from a draft quote. QuoteLine is a
// BaseEntity (no soft-delete column), so this is a hard remove via the parent's
// cascade — consistent with how other BaseEntity children are deleted. A quote
// must keep at least one line (the create flow requires NotEmpty), so deleting
// the final line is rejected.
public record DeleteQuoteLineCommand(int QuoteId, int LineId)
    : IRequest<QuoteDetailResponseModel>;

public class DeleteQuoteLineValidator : AbstractValidator<DeleteQuoteLineCommand>
{
    public DeleteQuoteLineValidator()
    {
        RuleFor(x => x.QuoteId).GreaterThan(0);
        RuleFor(x => x.LineId).GreaterThan(0);
    }
}

public class DeleteQuoteLineHandler(IQuoteRepository repo, AppDbContext db, IMediator mediator)
    : IRequestHandler<DeleteQuoteLineCommand, QuoteDetailResponseModel>
{
    public async Task<QuoteDetailResponseModel> Handle(DeleteQuoteLineCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindWithDetailsAsync(request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.QuoteId} not found");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only draft quotes can have lines removed.");

        var line = quote.Lines.FirstOrDefault(l => l.Id == request.LineId)
            ?? throw new KeyNotFoundException($"Quote line {request.LineId} not found");

        if (quote.Lines.Count == 1)
            throw new InvalidOperationException("A quote must have at least one line item.");

        quote.Lines.Remove(line);

        db.LogActivityAt(
            "line-removed",
            $"Removed quote line {line.LineNumber}: {line.Description}",
            ("Quote", quote.Id));

        await repo.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetQuoteByIdQuery(request.QuoteId), cancellationToken);
    }
}
