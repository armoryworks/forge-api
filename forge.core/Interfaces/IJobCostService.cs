using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IJobCostService
{
    Task<JobCostSummaryModel> GetCostSummaryAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualMaterialCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualLaborCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualBurdenCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualSubcontractCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetCurrentLaborRateAsync(int userId, DateTimeOffset asOf, CancellationToken ct);
    Task RecalculateTimeEntryCostsAsync(int jobId, CancellationToken ct);
}
