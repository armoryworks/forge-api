using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record GetPickWavesQuery(PickWaveStatus? Status, int? AssignedToId) : IRequest<List<PickWaveResponseModel>>;

public class GetPickWavesHandler(AppDbContext db) : IRequestHandler<GetPickWavesQuery, List<PickWaveResponseModel>>
{
    public async Task<List<PickWaveResponseModel>> Handle(GetPickWavesQuery request, CancellationToken cancellationToken)
    {
        var query = db.PickWaves.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(w => w.Status == request.Status.Value);

        if (request.AssignedToId.HasValue)
            query = query.Where(w => w.AssignedToId == request.AssignedToId.Value);

        var waves = await query
            .OrderByDescending(w => w.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);

        var names = await PickLineQueries.UserNamesAsync(db, waves.Select(w => w.AssignedToId), cancellationToken);
        return waves
            .Select(w => PickWaveResponses.Summary(w, w.AssignedToId is int a ? names.GetValueOrDefault(a) : null))
            .ToList();
    }
}
