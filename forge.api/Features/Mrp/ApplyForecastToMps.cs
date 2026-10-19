using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record ApplyForecastToMpsCommand(int ForecastId, int MasterScheduleId) : IRequest;

public class ApplyForecastToMpsHandler(AppDbContext db)
    : IRequestHandler<ApplyForecastToMpsCommand>
{
    public async Task Handle(ApplyForecastToMpsCommand request, CancellationToken cancellationToken)
    {
        var forecast = await db.DemandForecasts
            .FirstOrDefaultAsync(f => f.Id == request.ForecastId, cancellationToken)
            ?? throw new KeyNotFoundException($"Demand forecast {request.ForecastId} not found.");

        if (forecast.Status != ForecastStatus.Approved)
            throw new InvalidOperationException("Only approved forecasts can be applied to a master schedule.");

        var schedule = await db.MasterSchedules
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == request.MasterScheduleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Master schedule {request.MasterScheduleId} not found.");

        if (schedule.Status != MasterScheduleStatus.Draft)
            throw new InvalidOperationException("Forecasts can only be applied to draft master schedules.");

        if (string.IsNullOrEmpty(forecast.ForecastDataJson))
            throw new InvalidOperationException("Forecast has no data to apply.");

        var buckets = JsonSerializer.Deserialize<List<ForecastBucketModel>>(forecast.ForecastDataJson);
        if (buckets == null) return;

        var overrides = await db.ForecastOverrides
            .AsNoTracking()
            .Where(o => o.DemandForecastId == forecast.Id)
            .OrderBy(o => o.Id)
            .ToListAsync(cancellationToken);

        foreach (var bucket in buckets.Where(b => ForecastPeriod.IsForecast(b, forecast.ForecastStartDate)))
        {
            var overrideRow = overrides.LastOrDefault(o => ForecastPeriod.SameDate(o.PeriodStart, bucket.PeriodStart));
            var quantity = overrideRow?.OverrideQuantity ?? bucket.OverrideQuantity ?? bucket.ForecastedQuantity;
            if (quantity <= 0)
                continue;

            schedule.Lines.Add(new MasterScheduleLine
            {
                PartId = forecast.PartId,
                Quantity = quantity,
                DueDate = bucket.PeriodEnd.AddDays(-1),
                Notes = $"From forecast: {forecast.Name}",
            });
        }

        forecast.Status = ForecastStatus.Applied;
        forecast.AppliedToMasterScheduleId = schedule.Id;

        await db.SaveChangesAsync(cancellationToken);
    }
}
