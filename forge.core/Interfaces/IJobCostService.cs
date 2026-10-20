using Forge.Core.Entities;
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
    /// <summary>Prices the job's time entries that carry no cost yet (labor, actual labor and burden all zero), leaving
    /// already-priced entries alone. Run before absorbing conversion cost so entries logged before pricing ran are counted.</summary>
    Task PriceUnpricedTimeEntriesAsync(int jobId, CancellationToken ct);
    /// <summary>Sets one entry's labor, actual labor and burden cost from its duration (no save). Labor rate: the user's
    /// labor rate, else the work center's labor cost per hour, else the operation's labor rate; burden follows the
    /// costing policy the standard rollup uses.</summary>
    Task PriceTimeEntryAsync(TimeEntry entry, CancellationToken ct);
}
