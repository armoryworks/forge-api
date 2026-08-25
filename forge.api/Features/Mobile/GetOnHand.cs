using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.Mobile;

public record GetOnHandQuery(int PartId, int LocationId) : IRequest<OnHandResponseModel>;

/// <summary>Quantity on hand for a part at a bin — the stepper default — plus lots when the part is lot-tracked.</summary>
public class GetOnHandHandler(AppDbContext db) : IRequestHandler<GetOnHandQuery, OnHandResponseModel>
{
    public async Task<OnHandResponseModel> Handle(GetOnHandQuery request, CancellationToken ct)
    {
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == request.PartId)
            .Select(p => new { p.PartNumber, p.TraceabilityType })
            .FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var location = await db.StorageLocations.AsNoTracking()
            .Where(l => l.Id == request.LocationId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException($"Location {request.LocationId} not found");

        var contents = await db.BinContents.AsNoTracking()
            .Where(b => b.LocationId == request.LocationId
                && b.EntityType == "part" && b.EntityId == request.PartId
                && b.RemovedAt == null && b.Quantity > 0)
            .Select(b => new { b.LotNumber, b.Quantity })
            .ToListAsync(ct);

        var lots = contents
            .Where(c => !string.IsNullOrEmpty(c.LotNumber))
            .GroupBy(c => c.LotNumber!)
            .Select(g => new OnHandLotModel(g.Key, g.Sum(c => c.Quantity)))
            .OrderBy(l => l.LotNumber)
            .ToList();

        return new OnHandResponseModel(
            request.PartId, part.PartNumber, request.LocationId, location,
            contents.Sum(c => c.Quantity),
            part.TraceabilityType != TraceabilityType.None,
            lots);
    }
}
