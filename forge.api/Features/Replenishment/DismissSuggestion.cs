using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Replenishment;

public record DismissSuggestionCommand(int SuggestionId, int UserId, string Reason) : IRequest;

public class DismissSuggestionValidator : AbstractValidator<DismissSuggestionCommand>
{
    public DismissSuggestionValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when dismissing a suggestion")
            .MaximumLength(500);
    }
}

public class DismissSuggestionHandler(AppDbContext db)
    : IRequestHandler<DismissSuggestionCommand>
{
    public async Task Handle(DismissSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await db.ReorderSuggestions
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Reorder suggestion {request.SuggestionId} not found");

        if (suggestion.Status != ReorderSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion is already {suggestion.Status}");

        suggestion.Status = ReorderSuggestionStatus.Dismissed;
        suggestion.DismissedByUserId = request.UserId;
        suggestion.DismissedAt = DateTimeOffset.UtcNow;
        suggestion.DismissReason = request.Reason;

        await db.SaveChangesAsync(cancellationToken);
    }
}
