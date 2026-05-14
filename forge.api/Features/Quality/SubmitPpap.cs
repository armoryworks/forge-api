using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record SubmitPpapCommand(int Id) : IRequest;

public class SubmitPpapHandler(AppDbContext db, IClock clock)
    : IRequestHandler<SubmitPpapCommand>
{
    public async Task Handle(SubmitPpapCommand command, CancellationToken cancellationToken)
    {
        var submission = await db.PpapSubmissions
            .Include(s => s.Elements)
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP submission {command.Id} not found");

        // Validate all required elements are complete
        var incompleteRequired = submission.Elements
            .Where(e => e.IsRequired && e.Status != PpapElementStatus.Complete && e.Status != PpapElementStatus.NotApplicable)
            .ToList();

        if (incompleteRequired.Count > 0)
        {
            var names = string.Join(", ", incompleteRequired.Select(e => e.ElementName));
            throw new InvalidOperationException($"Cannot submit PPAP: the following required elements are incomplete: {names}");
        }

        submission.Status = PpapStatus.Submitted;
        submission.SubmittedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
