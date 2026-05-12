using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record GetMachineTagLatestQuery(int WorkCenterId) : IRequest<List<MachineDataPointResponseModel>>;

public class GetMachineTagLatestHandler(AppDbContext db)
    : IRequestHandler<GetMachineTagLatestQuery, List<MachineDataPointResponseModel>>
{
    public async Task<List<MachineDataPointResponseModel>> Handle(
        GetMachineTagLatestQuery request, CancellationToken cancellationToken)
    {
        var tags = await db.MachineTags
            .AsNoTracking()
            .Include(t => t.Connection)
            .Where(t => t.Connection.WorkCenterId == request.WorkCenterId && t.IsActive)
            .ToListAsync(cancellationToken);

        if (tags.Count == 0)
            return [];

        var tagIds = tags.Select(t => t.Id).ToList();

        var latestPoints = await db.MachineDataPoints
            .AsNoTracking()
            .Where(d => tagIds.Contains(d.TagId))
            .GroupBy(d => d.TagId)
            .Select(g => g.OrderByDescending(d => d.Timestamp).First())
            .ToListAsync(cancellationToken);

        var tagLookup = tags.ToDictionary(t => t.Id);

        return latestPoints.Select(d =>
        {
            tagLookup.TryGetValue(d.TagId, out var tag);
            return new MachineDataPointResponseModel
            {
                TagId = d.TagId,
                TagName = tag?.TagName ?? string.Empty,
                Value = d.Value,
                Timestamp = d.Timestamp,
                Unit = tag?.Unit,
                Quality = d.Quality,
            };
        }).ToList();
    }
}
