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

        // Add forecast buckets as MPS lines (only future periods with forecasted quantities)
        foreach (var bucket in buckets.Where(b => b.ForecastedQuantity > 0))
        {
            // Use override quantity if available
            var quantity = bucket.OverrideQuantity ?? bucket.ForecastedQuantity;

            schedule.Lines.Add(new MasterScheduleLine
            {
                PartId = forecast.PartId,
                Quantity = quantity,
                DueDate = bucket.PeriodEnd.AddDays(-1), // Due by end of period
                Notes = $"From forecast: {forecast.Name}",
            });
        }

        forecast.Status = ForecastStatus.Applied;
        forecast.AppliedToMasterScheduleId = schedule.Id;

        await db.SaveChangesAsync(cancellationToken);
    }
}
