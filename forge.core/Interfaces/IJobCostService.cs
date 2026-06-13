using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IJobCostService
{
    Task<JobCostSummaryModel> GetCostSummaryAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualMaterialCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualLaborCostAsync(int jobId, CancellationToken ct);
    /// <summary>Labor cost at the actual burdened rate (Σ ActualLaborCost, falling back to the standard-rate
    /// LaborCost where no actual rate has been applied). Used to absorb labor into WIP at actual cost.</summary>
    Task<decimal> GetActualLaborCostAtActualRateAsync(int jobId, CancellationToken ct);
    /// <summary>Labor RATE variance: Σ (actual-rate − standard-rate) × hours = Σ (ActualLaborCost − LaborCost),
    /// counting only entries that carry an actual-rate cost. Positive = unfavorable (paid above standard).</summary>
    Task<decimal> GetLaborRateVarianceAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualBurdenCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetActualSubcontractCostAsync(int jobId, CancellationToken ct);
    Task<decimal> GetCurrentLaborRateAsync(int userId, DateTimeOffset asOf, CancellationToken ct);
    Task RecalculateTimeEntryCostsAsync(int jobId, CancellationToken ct);
}
