using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.AutoPo;

public record DismissAutoPoSuggestionCommand(int SuggestionId) : IRequest;

public class DismissAutoPoSuggestionHandler(AppDbContext db) : IRequestHandler<DismissAutoPoSuggestionCommand>
{
    public async Task Handle(DismissAutoPoSuggestionCommand request, CancellationToken ct)
    {
        var suggestion = await db.AutoPoSuggestions
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, ct)
            ?? throw new KeyNotFoundException($"Auto-PO suggestion {request.SuggestionId} not found");

        if (suggestion.Status != AutoPoSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion {request.SuggestionId} is already {suggestion.Status}");

        suggestion.Status = AutoPoSuggestionStatus.Dismissed;
        await db.SaveChangesAsync(ct);
    }
}
