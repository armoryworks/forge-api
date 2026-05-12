using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateFmeaCommand(int Id, UpdateFmeaRequestModel Request) : IRequest<FmeaResponseModel>;

public class UpdateFmeaHandler(AppDbContext db, IMediator mediator)
    : IRequestHandler<UpdateFmeaCommand, FmeaResponseModel>
{
    public async Task<FmeaResponseModel> Handle(
        UpdateFmeaCommand command, CancellationToken cancellationToken)
    {
        var fmea = await db.FmeaAnalyses
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"FMEA {command.Id} not found");

        var req = command.Request;

        if (req.Name != null)
            fmea.Name = req.Name;
        if (req.Status.HasValue)
            fmea.Status = req.Status.Value;
        if (req.PreparedBy != null)
            fmea.PreparedBy = req.PreparedBy;
        if (req.Responsibility != null)
            fmea.Responsibility = req.Responsibility;
        if (req.RevisionDate.HasValue)
        {
            fmea.RevisionDate = req.RevisionDate.Value;
            fmea.RevisionNumber++;
        }
        if (req.Notes != null)
            fmea.Notes = req.Notes;

        await db.SaveChangesAsync(cancellationToken);

        return await mediator.Send(new GetFmeaQuery(command.Id), cancellationToken);
    }
}
