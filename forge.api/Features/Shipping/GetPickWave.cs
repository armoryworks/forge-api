using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Shipping;

public record GetPickWaveQuery(int Id) : IRequest<PickWaveResponseModel>;

public class GetPickWaveHandler(AppDbContext db) : IRequestHandler<GetPickWaveQuery, PickWaveResponseModel>
{
    public async Task<PickWaveResponseModel> Handle(GetPickWaveQuery request, CancellationToken cancellationToken)
    {
        var wave = await db.PickWaves
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pick wave {request.Id} not found");

        var lines = await PickLineQueries.ListAsync(db, db.PickLines.Where(l => l.WaveId == wave.Id), cancellationToken);
        var names = await PickLineQueries.UserNamesAsync(db, [wave.AssignedToId], cancellationToken);

        return PickWaveResponses.Summary(wave, wave.AssignedToId is int a ? names.GetValueOrDefault(a) : null) with { Lines = lines };
    }
}
