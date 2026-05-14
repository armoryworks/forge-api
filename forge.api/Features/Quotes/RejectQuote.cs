using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Quotes;

public record RejectQuoteCommand(int Id) : IRequest;

public class RejectQuoteHandler(IQuoteRepository repo)
    : IRequestHandler<RejectQuoteCommand>
{
    public async Task Handle(RejectQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Sent)
            throw new InvalidOperationException("Only Sent quotes can be rejected");

        quote.Status = QuoteStatus.Declined;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
