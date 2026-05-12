using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IForecastService
{
    Task<DemandForecastResponseModel> GenerateForecastAsync(
        int partId,
        string name,
        ForecastMethod method,
        int historicalPeriods,
        int forecastPeriods,
        double? smoothingFactor,
        int? createdByUserId,
        CancellationToken cancellationToken = default);
}
