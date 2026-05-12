using MediatR;
using Forge.Core.Enums;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.Quotes;

public record SendQuoteCommand(int Id) : IRequest;

public class SendQuoteHandler(IQuoteRepository repo)
    : IRequestHandler<SendQuoteCommand>
{
    public async Task Handle(SendQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Quote {request.Id} not found");

        if (quote.Status != QuoteStatus.Draft)
            throw new InvalidOperationException("Only Draft quotes can be sent");

        quote.Status = QuoteStatus.Sent;
        quote.SentDate = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
