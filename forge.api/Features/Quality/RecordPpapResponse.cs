using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record RecordPpapResponseCommand(int Id, RecordPpapResponseRequestModel Request) : IRequest;

public class RecordPpapResponseValidator : AbstractValidator<RecordPpapResponseCommand>
{
    public RecordPpapResponseValidator()
    {
        RuleFor(x => x.Request.CustomerDecision)
            .Must(d => d is PpapStatus.Approved or PpapStatus.Rejected or PpapStatus.Interim)
            .WithMessage("Customer decision must be Approved, Rejected, or Interim");
    }
}

public class RecordPpapResponseHandler(AppDbContext db, IClock clock)
    : IRequestHandler<RecordPpapResponseCommand>
{
    public async Task Handle(RecordPpapResponseCommand command, CancellationToken cancellationToken)
    {
        var submission = await db.PpapSubmissions
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP submission {command.Id} not found");

        if (submission.Status != PpapStatus.Submitted)
            throw new InvalidOperationException("Can only record a response for a submitted PPAP");

        submission.Status = command.Request.CustomerDecision;
        submission.CustomerResponseNotes = command.Request.Notes;

        if (command.Request.CustomerDecision == PpapStatus.Approved)
            submission.ApprovedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
