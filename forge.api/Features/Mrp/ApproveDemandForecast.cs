using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;

namespace Forge.Api.Features.Mrp;

public record ApproveDemandForecastCommand(int Id) : IRequest;

public class ApproveDemandForecastHandler(AppDbContext db)
    : IRequestHandler<ApproveDemandForecastCommand>
{
    public async Task Handle(ApproveDemandForecastCommand request, CancellationToken cancellationToken)
    {
        var forecast = await db.DemandForecasts
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Demand forecast {request.Id} not found.");

        if (forecast.Status != ForecastStatus.Draft)
            throw new InvalidOperationException("Only draft forecasts can be approved.");

        forecast.Status = ForecastStatus.Approved;
        await db.SaveChangesAsync(cancellationToken);
    }
}
