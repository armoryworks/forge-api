using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Quotes;

public record AcceptQuoteCommand(int Id) : IRequest;

public class AcceptQuoteHandler(IQuoteRepository repo)
    : IRequestHandler<AcceptQuoteCommand>
{
    public async Task Handle(AcceptQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Sent)
            throw new InvalidOperationException("Only Sent quotes can be accepted");

        quote.Status = QuoteStatus.Accepted;
        quote.AcceptedDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
