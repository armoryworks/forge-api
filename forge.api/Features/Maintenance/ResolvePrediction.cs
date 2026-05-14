using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record ResolvePredictionCommand(int Id, ResolvePredictionRequestModel Request) : IRequest;

public class ResolvePredictionHandler(AppDbContext db)
    : IRequestHandler<ResolvePredictionCommand>
{
    public async Task Handle(ResolvePredictionCommand command, CancellationToken cancellationToken)
    {
        var prediction = await db.MaintenancePredictions
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {command.Id} not found");

        prediction.Status = MaintenancePredictionStatus.Resolved;
        prediction.ResolutionNotes = command.Request.Notes;

        await db.SaveChangesAsync(cancellationToken);
    }
}
