using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Checkpoints;

/// <summary>Returns null (-> 404 at the controller) when no checkpoint has ever
/// been PUT for this WorldId — a real, expected state (first save of a new
/// world), not an error.</summary>
public record GetCheckpointQuery(string WorldId) : IRequest<CheckpointResponseModel?>;

public class GetCheckpointHandler(AppDbContext db) : IRequestHandler<GetCheckpointQuery, CheckpointResponseModel?>
{
    public async Task<CheckpointResponseModel?> Handle(GetCheckpointQuery request, CancellationToken ct)
    {
        var row = await db.Checkpoints.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorldId == request.WorldId, ct);

        return row is null ? null : new CheckpointResponseModel(row.WorldId, row.Blob, row.UpdatedAt);
    }
}
