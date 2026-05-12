using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Serials;

public record GetSerialHistoryQuery(int SerialId) : IRequest<List<SerialHistoryResponseModel>>;

public class GetSerialHistoryHandler(AppDbContext db) : IRequestHandler<GetSerialHistoryQuery, List<SerialHistoryResponseModel>>
{
    public async Task<List<SerialHistoryResponseModel>> Handle(GetSerialHistoryQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.SerialNumbers.AnyAsync(s => s.Id == request.SerialId, cancellationToken);
        if (!exists)
            throw new KeyNotFoundException($"Serial number {request.SerialId} not found");

        return await db.Set<Forge.Core.Entities.SerialHistory>()
            .AsNoTracking()
            .Where(h => h.SerialNumberId == request.SerialId)
            .OrderByDescending(h => h.OccurredAt)
            .Select(h => new SerialHistoryResponseModel(
                h.Id,
                h.SerialNumberId,
                h.Action,
                h.FromLocationName,
                h.ToLocationName,
                h.ActorId,
                h.Details,
                h.OccurredAt))
            .ToListAsync(cancellationToken);
    }
}
