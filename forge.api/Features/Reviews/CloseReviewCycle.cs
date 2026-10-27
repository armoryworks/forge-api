using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Reviews;

public record CloseReviewCycleCommand(int Id) : IRequest;

/// <summary>Closes a cycle and every review in it that is not closed yet, whatever its stage.</summary>
public class CloseReviewCycleHandler(AppDbContext db, IClock clock) : IRequestHandler<CloseReviewCycleCommand>
{
    public async Task Handle(CloseReviewCycleCommand request, CancellationToken ct)
    {
        var cycle = await db.ReviewCycles.FirstOrDefaultAsync(c => c.Id == request.Id, ct)
            ?? throw new KeyNotFoundException($"Review cycle {request.Id} not found");
        if (cycle.Status == ReviewCycleStatus.Completed)
            throw new InvalidOperationException("This review cycle is already closed.");

        var now = clock.UtcNow;
        var open = await db.PerformanceReviews
            .Where(r => r.CycleId == cycle.Id && r.Status != ReviewStatus.Closed && r.Status != ReviewStatus.Completed)
            .ToListAsync(ct);
        foreach (var review in open)
        {
            db.LogActivityAt("closed", $"Closed with the cycle at {review.Status}", ("PerformanceReview", review.Id));
            review.Status = ReviewStatus.Closed;
            review.CompletedAt ??= now;
        }

        cycle.Status = ReviewCycleStatus.Completed;
        cycle.ClosedAt = now;
        db.LogActivityAt("closed", $"Review cycle closed; {open.Count} open review(s) closed", ("ReviewCycle", cycle.Id));
        await db.SaveChangesAsync(ct);
    }
}
