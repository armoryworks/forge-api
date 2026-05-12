using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetDemandForecastsQuery(int? PartId = null) : IRequest<List<DemandForecastResponseModel>>;

public class GetDemandForecastsHandler(AppDbContext db)
    : IRequestHandler<GetDemandForecastsQuery, List<DemandForecastResponseModel>>
{
    public async Task<List<DemandForecastResponseModel>> Handle(GetDemandForecastsQuery request, CancellationToken cancellationToken)
    {
        var query = db.DemandForecasts.AsNoTracking()
            .Include(f => f.Part)
            .AsQueryable();

        if (request.PartId.HasValue)
            query = query.Where(f => f.PartId == request.PartId.Value);

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new DemandForecastResponseModel(
                f.Id,
                f.Name,
                f.PartId,
                f.Part!.PartNumber,
                f.Part.Description,
                f.Method,
                f.Status,
                f.HistoricalPeriods,
                f.ForecastPeriods,
                f.SmoothingFactor,
                f.ForecastStartDate,
                null,
                f.AppliedToMasterScheduleId,
                f.Overrides.Count,
                f.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }
}
