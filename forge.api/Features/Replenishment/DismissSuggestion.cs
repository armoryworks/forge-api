using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
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

public class DismissSuggestionHandler(AppDbContext db, IClock clock)
    : IRequestHandler<DismissSuggestionCommand>
{
    public async Task Handle(DismissSuggestionCommand request, CancellationToken cancellationToken)
    {
        var suggestion = await db.ReorderSuggestions
            .FirstOrDefaultAsync(s => s.Id == request.SuggestionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Reorder suggestion {request.SuggestionId} not found");

        if (suggestion.Status != ReorderSuggestionStatus.Pending)
            throw new InvalidOperationException($"Suggestion is already {suggestion.Status}");

        var now = clock.UtcNow;
        suggestion.Status = ReorderSuggestionStatus.Dismissed;
        suggestion.DismissedByUserId = request.UserId;
        suggestion.DismissedAt = now;
        suggestion.DismissReason = request.Reason;

        await ReplenishmentAssignee.CloseTasksAsync(
            db, [suggestion.Id], FollowUpStatus.Dismissed, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }
}
