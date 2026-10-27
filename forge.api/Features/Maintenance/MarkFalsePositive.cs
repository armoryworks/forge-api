using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Maintenance;

public record MarkFalsePositiveCommand(int Id, ResolvePredictionRequestModel Request) : IRequest;

public class MarkFalsePositiveHandler(AppDbContext db, IClock clock)
    : IRequestHandler<MarkFalsePositiveCommand>
{
    public async Task Handle(MarkFalsePositiveCommand command, CancellationToken cancellationToken)
    {
        var prediction = await db.MaintenancePredictions
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Prediction {command.Id} not found");

        prediction.Status = MaintenancePredictionStatus.FalsePositive;
        prediction.ResolutionNotes = command.Request.Notes;
        prediction.WasAccurate = false;
        prediction.ResolvedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
