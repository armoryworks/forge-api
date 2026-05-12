using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.IoT;

public record GetMachineTagHistoryQuery(
    int WorkCenterId,
    int? TagId,
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<List<MachineDataPointResponseModel>>;

public class GetMachineTagHistoryHandler(AppDbContext db)
    : IRequestHandler<GetMachineTagHistoryQuery, List<MachineDataPointResponseModel>>
{
    public async Task<List<MachineDataPointResponseModel>> Handle(
        GetMachineTagHistoryQuery request, CancellationToken cancellationToken)
    {
        var query = db.MachineDataPoints
            .AsNoTracking()
            .Include(d => d.Tag)
            .Where(d => d.WorkCenterId == request.WorkCenterId
                && d.Timestamp >= request.From
                && d.Timestamp <= request.To);

        if (request.TagId.HasValue)
            query = query.Where(d => d.TagId == request.TagId.Value);

        var points = await query
            .OrderBy(d => d.Timestamp)
            .Take(10000)
            .ToListAsync(cancellationToken);

        return points.Select(d => new MachineDataPointResponseModel
        {
            TagId = d.TagId,
            TagName = d.Tag.TagName,
            Value = d.Value,
            Timestamp = d.Timestamp,
            Unit = d.Tag.Unit,
            Quality = d.Quality,
        }).ToList();
    }
}
