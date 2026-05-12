using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Parts;

public record GetPartInventorySummaryQuery(int PartId) : IRequest<PartInventorySummaryResponseModel>;

public class GetPartInventorySummaryHandler(AppDbContext db)
    : IRequestHandler<GetPartInventorySummaryQuery, PartInventorySummaryResponseModel>
{
    public async Task<PartInventorySummaryResponseModel> Handle(
        GetPartInventorySummaryQuery request, CancellationToken ct)
    {
        var part = await db.Parts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PartId, ct)
            ?? throw new KeyNotFoundException($"Part {request.PartId} not found");

        var binContents = await db.BinContents
            .AsNoTracking()
            .Include(bc => bc.Location)
            .Where(bc => bc.EntityType == "part" && bc.EntityId == request.PartId && bc.RemovedAt == null)
            .ToListAsync(ct);

        var allLocations = await db.StorageLocations
            .AsNoTracking()
            .Where(l => l.DeletedAt == null)
            .ToListAsync(ct);

        var locById = allLocations.ToDictionary(l => l.Id);

        var totalQuantity = binContents.Sum(bc => bc.Quantity);

        var binLocations = binContents
            .Select(bc => new PartBinLocationResponseModel(
                BuildPath(bc.Location, locById),
                bc.Quantity))
            .ToList();

        return new PartInventorySummaryResponseModel(totalQuantity, binLocations);
    }

    private static string BuildPath(StorageLocation loc, Dictionary<int, StorageLocation> byId)
    {
        var parts = new List<string> { loc.Name };
        var current = loc;
        while (current.ParentId.HasValue && byId.TryGetValue(current.ParentId.Value, out var parent))
        {
            parts.Insert(0, parent.Name);
            current = parent;
        }
        return string.Join(" / ", parts);
    }
}
