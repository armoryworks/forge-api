using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record DeleteProductionRunCommand(int JobId, int RunId) : IRequest;

public class DeleteProductionRunHandler(AppDbContext db, IClock clock) : IRequestHandler<DeleteProductionRunCommand>
{
    public async Task Handle(DeleteProductionRunCommand request, CancellationToken cancellationToken)
    {
        var run = await db.ProductionRuns
            .FirstOrDefaultAsync(pr => pr.Id == request.RunId && pr.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Production run {request.RunId} not found on job {request.JobId}.");

        if (run.ReceivedToStockAt is not null)
            throw new InvalidOperationException(
                $"Production run {run.RunNumber} has been received to stock, so it cannot be deleted.");

        await ProductionRunTooling.RemoveShotsAsync(db, run, cancellationToken);
        run.DeletedAt = clock.UtcNow;
        db.LogActivityAt("production-run-deleted", $"Production run {run.RunNumber} deleted", ("Job", run.JobId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
