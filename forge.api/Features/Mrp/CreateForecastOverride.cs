using FluentValidation;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record CreateForecastOverrideCommand(
    int ForecastId,
    DateTimeOffset PeriodStart,
    decimal OverrideQuantity,
    string? Reason,
    int? OverriddenByUserId
) : IRequest<ForecastOverrideResponseModel>;

public class CreateForecastOverrideValidator : AbstractValidator<CreateForecastOverrideCommand>
{
    public CreateForecastOverrideValidator()
    {
        RuleFor(x => x.ForecastId).GreaterThan(0);
        RuleFor(x => x.OverrideQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public class CreateForecastOverrideHandler(AppDbContext db)
    : IRequestHandler<CreateForecastOverrideCommand, ForecastOverrideResponseModel>
{
    public async Task<ForecastOverrideResponseModel> Handle(CreateForecastOverrideCommand request, CancellationToken cancellationToken)
    {
        var forecast = await db.DemandForecasts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.ForecastId, cancellationToken)
            ?? throw new KeyNotFoundException($"Demand forecast {request.ForecastId} not found.");

        if (forecast.Status == ForecastStatus.Applied || forecast.Status == ForecastStatus.Expired)
            throw new InvalidOperationException($"Cannot override a forecast with status '{forecast.Status}'.");

        var buckets = string.IsNullOrEmpty(forecast.ForecastDataJson)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<List<ForecastBucketModel>>(forecast.ForecastDataJson);
        var matchingBucket = buckets?.FirstOrDefault(b => ForecastPeriod.SameDate(b.PeriodStart, request.PeriodStart))
            ?? throw new InvalidOperationException($"The forecast has no period starting {request.PeriodStart.UtcDateTime:MM/dd/yyyy}.");

        if (!ForecastPeriod.IsForecast(matchingBucket, forecast.ForecastStartDate))
            throw new InvalidOperationException("Only forecast periods can be overridden; history periods are actual demand.");

        var forecastOverride = new ForecastOverride
        {
            DemandForecastId = request.ForecastId,
            PeriodStart = matchingBucket.PeriodStart,
            OriginalQuantity = matchingBucket.ForecastedQuantity,
            OverrideQuantity = request.OverrideQuantity,
            Reason = request.Reason,
            OverriddenByUserId = request.OverriddenByUserId,
        };

        db.ForecastOverrides.Add(forecastOverride);
        await db.SaveChangesAsync(cancellationToken);

        return new ForecastOverrideResponseModel(
            forecastOverride.Id,
            forecastOverride.DemandForecastId,
            forecastOverride.PeriodStart,
            forecastOverride.OriginalQuantity,
            forecastOverride.OverrideQuantity,
            forecastOverride.Reason,
            forecastOverride.OverriddenByUserId
        );
    }
}
