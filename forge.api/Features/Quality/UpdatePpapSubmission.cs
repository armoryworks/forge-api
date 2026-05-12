using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdatePpapSubmissionCommand(int Id, UpdatePpapSubmissionRequestModel Request) : IRequest<PpapSubmissionResponseModel>;

public class UpdatePpapSubmissionHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdatePpapSubmissionCommand, PpapSubmissionResponseModel>
{
    public async Task<PpapSubmissionResponseModel> Handle(
        UpdatePpapSubmissionCommand command, CancellationToken cancellationToken)
    {
        var submission = await db.PpapSubmissions
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"PPAP submission {command.Id} not found");

        var req = command.Request;

        if (req.PpapLevel.HasValue)
            submission.PpapLevel = req.PpapLevel.Value;
        if (req.PartRevision != null)
            submission.PartRevision = req.PartRevision;
        if (req.DueDate.HasValue)
            submission.DueDate = req.DueDate;
        if (req.CustomerContactName != null)
            submission.CustomerContactName = req.CustomerContactName;
        if (req.CustomerResponseNotes != null)
            submission.CustomerResponseNotes = req.CustomerResponseNotes;
        if (req.InternalNotes != null)
            submission.InternalNotes = req.InternalNotes;

        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetPpapSubmissionQuery(command.Id), cancellationToken);
    }
}
