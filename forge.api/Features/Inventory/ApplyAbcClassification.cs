using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record ApplyAbcClassificationCommand(int RunId) : IRequest;

public class ApplyAbcClassificationHandler(AppDbContext db) : IRequestHandler<ApplyAbcClassificationCommand>
{
    public async Task Handle(ApplyAbcClassificationCommand request, CancellationToken cancellationToken)
    {
        var run = await db.AbcClassificationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RunId, cancellationToken)
            ?? throw new KeyNotFoundException($"ABC classification run {request.RunId} not found");

        // Note: Applying classification to Part.AbcClassification field is deferred
        // as adding fields to Part is a pervasive change. This handler is a placeholder
        // for when that field is added.

        await Task.CompletedTask;
    }
}
