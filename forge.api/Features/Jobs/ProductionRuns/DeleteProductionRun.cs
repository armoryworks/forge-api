using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.ProductionRuns;

public record DeleteProductionRunCommand(int JobId, int RunId) : IRequest;

public class DeleteProductionRunHandler(AppDbContext db) : IRequestHandler<DeleteProductionRunCommand>
{
    public async Task Handle(DeleteProductionRunCommand request, CancellationToken cancellationToken)
    {
        var run = await db.ProductionRuns
            .FirstOrDefaultAsync(pr => pr.Id == request.RunId && pr.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Production run {request.RunId} not found on job {request.JobId}.");

        run.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
