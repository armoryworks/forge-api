using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Training;

// F-14-BE-01: admin soft-delete of a training path.
public sealed record DeleteTrainingPathCommand(int Id) : IRequest;

public sealed class DeleteTrainingPathHandler(AppDbContext db) : IRequestHandler<DeleteTrainingPathCommand>
{
    public async Task Handle(DeleteTrainingPathCommand request, CancellationToken cancellationToken)
    {
        var path = await db.TrainingPaths.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Training path {request.Id} not found");

        path.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
