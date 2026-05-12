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

        return await query
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new PickWaveResponseModel
            {
                Id = w.Id,
                WaveNumber = w.WaveNumber,
                Status = w.Status,
                Strategy = w.Strategy,
                AssignedToId = w.AssignedToId,
                TotalLines = w.TotalLines,
                PickedLines = w.PickedLines,
                ReleasedAt = w.ReleasedAt,
                StartedAt = w.StartedAt,
                CompletedAt = w.CompletedAt,
                Notes = w.Notes,
                CreatedAt = w.CreatedAt,
            })
            .ToListAsync(cancellationToken);
    }
}
