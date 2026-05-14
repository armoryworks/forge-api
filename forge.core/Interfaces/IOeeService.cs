using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IOeeService
{
    Task<OeeCalculationModel> CalculateOeeAsync(int workCenterId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<OeeCalculationModel>> CalculateOeeForAllWorkCentersAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<IReadOnlyList<OeeTrendPointModel>> GetOeeTrendAsync(int workCenterId, DateOnly from, DateOnly to, OeeTrendGranularity granularity, CancellationToken ct);
    Task<SixBigLossesModel> GetSixBigLossesAsync(int workCenterId, DateOnly from, DateOnly to, CancellationToken ct);
}
