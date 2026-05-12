using FluentValidation;

using MediatR;

using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Mrp;

public record GenerateDemandForecastCommand(
    int PartId,
    string Name,
    ForecastMethod Method,
    int HistoricalPeriods,
    int ForecastPeriods,
    double? SmoothingFactor,
    int? CreatedByUserId
) : IRequest<DemandForecastResponseModel>;

public class GenerateDemandForecastValidator : AbstractValidator<GenerateDemandForecastCommand>
{
    public GenerateDemandForecastValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.HistoricalPeriods).InclusiveBetween(3, 36);
        RuleFor(x => x.ForecastPeriods).InclusiveBetween(1, 24);
        RuleFor(x => x.SmoothingFactor)
            .InclusiveBetween(0.01, 0.99)
            .When(x => x.Method == ForecastMethod.ExponentialSmoothing);
    }
}

public class GenerateDemandForecastHandler(IForecastService forecastService)
    : IRequestHandler<GenerateDemandForecastCommand, DemandForecastResponseModel>
{
    public Task<DemandForecastResponseModel> Handle(GenerateDemandForecastCommand request, CancellationToken cancellationToken)
    {
        return forecastService.GenerateForecastAsync(
            request.PartId,
            request.Name,
            request.Method,
            request.HistoricalPeriods,
            request.ForecastPeriods,
            request.SmoothingFactor,
            request.CreatedByUserId,
            cancellationToken);
    }
}
