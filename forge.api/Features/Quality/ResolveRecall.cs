using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record ResolveRecallCommand(int Id, ResolveRecallRequestModel Data) : IRequest<RecallDetailResponseModel>;

public class ResolveRecallHandler(AppDbContext db) : IRequestHandler<ResolveRecallCommand, RecallDetailResponseModel>
{
    public async Task<RecallDetailResponseModel> Handle(ResolveRecallCommand request, CancellationToken cancellationToken)
    {
        var recall = await db.Recalls.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Recall {request.Id} not found.");

        recall.Status = RecallStatus.Resolved;
        recall.ResolvedAt = DateTimeOffset.UtcNow;
        recall.ResolutionNotes = request.Data.ResolutionNotes?.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return await RecallMapping.LoadDetailAsync(db, recall.Id, cancellationToken);
    }
}
