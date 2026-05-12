using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface ISankeyReportRepository
{
    Task<List<SankeyFlowItem>> GetQuoteToCashFlowAsync(DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct);
    Task<List<SankeyFlowItem>> GetJobStageFlowAsync(CancellationToken ct);
    Task<List<SankeyFlowItem>> GetMaterialToProductFlowAsync(CancellationToken ct);
    Task<List<SankeyFlowItem>> GetWorkerOrdersFlowAsync(CancellationToken ct);
    Task<List<SankeyFlowItem>> GetExpenseFlowAsync(DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct);
    Task<List<SankeyFlowItem>> GetVendorSupplyChainFlowAsync(CancellationToken ct);
    Task<List<SankeyFlowItem>> GetQualityRejectionFlowAsync(DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct);
    Task<List<SankeyFlowItem>> GetInventoryLocationFlowAsync(CancellationToken ct);
    Task<List<SankeyFlowItem>> GetCustomerRevenueFlowAsync(DateTimeOffset? start, DateTimeOffset? end, CancellationToken ct);
    Task<List<SankeyFlowItem>> GetTrainingCompletionFlowAsync(CancellationToken ct);
}
