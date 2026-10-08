using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IJobOperationService
{
    Task<bool> IsTrackingEnabledAsync(CancellationToken ct);

    Task<(Job Job, Operation Operation)> FindRoutingStepAsync(int jobId, int operationId, CancellationToken ct);

    Task<decimal> GetJobQuantityAsync(Job job, CancellationToken ct);

    Task<JobOperation> EnsureRowAsync(Job job, Operation operation, CancellationToken ct);

    Task<JobOperationsResponseModel> BuildAsync(int jobId, CancellationToken ct);

    Task<IReadOnlyDictionary<int, JobOperationSummaryResponseModel>> SummarizeAsync(
        IReadOnlyCollection<int> jobIds, CancellationToken ct);
}
