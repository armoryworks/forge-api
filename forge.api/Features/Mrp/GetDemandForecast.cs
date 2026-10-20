using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record GetDemandForecastQuery(int Id) : IRequest<DemandForecastResponseModel>;

public class GetDemandForecastHandler(AppDbContext db)
    : IRequestHandler<GetDemandForecastQuery, DemandForecastResponseModel>
{
    public async Task<DemandForecastResponseModel> Handle(GetDemandForecastQuery request, CancellationToken cancellationToken)
    {
        var forecast = await db.DemandForecasts.AsNoTracking()
            .Include(f => f.Part)
            .Include(f => f.Overrides)
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Demand forecast {request.Id} not found.");

        var buckets = string.IsNullOrEmpty(forecast.ForecastDataJson)
            ? []
            : JsonSerializer.Deserialize<List<ForecastBucketModel>>(forecast.ForecastDataJson) ?? [];
        var overrides = forecast.Overrides.OrderBy(o => o.Id).ToList();

        var merged = buckets
            .Select(b => overrides.LastOrDefault(o => ForecastPeriod.SameDate(o.PeriodStart, b.PeriodStart)) is { } o
                ? b with { OverrideQuantity = o.OverrideQuantity }
                : b)
            .ToList();

        return new DemandForecastResponseModel(
            forecast.Id,
            forecast.Name,
            forecast.PartId,
            forecast.Part.PartNumber,
            forecast.Part.Description,
            forecast.Method,
            forecast.Status,
            forecast.HistoricalPeriods,
            forecast.ForecastPeriods,
            forecast.SmoothingFactor,
            forecast.ForecastStartDate,
            merged,
            forecast.AppliedToMasterScheduleId,
            overrides.Count,
            forecast.CreatedAt);
    }
}
