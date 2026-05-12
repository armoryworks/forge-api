using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IProjectAccountingService
{
    Task<Project> CreateProjectAsync(CreateProjectRequestModel request, CancellationToken ct);
    Task<Project> UpdateProjectAsync(int projectId, UpdateProjectRequestModel request, CancellationToken ct);
    Task<WbsElement> AddWbsElementAsync(int projectId, CreateWbsElementRequestModel request, CancellationToken ct);
    Task<WbsElement> UpdateWbsElementAsync(int elementId, UpdateWbsElementRequestModel request, CancellationToken ct);
    Task DeleteWbsElementAsync(int elementId, CancellationToken ct);
    Task AddCostEntryAsync(int elementId, WbsCostEntry entry, CancellationToken ct);
    Task RecalculateProjectTotalsAsync(int projectId, CancellationToken ct);
    Task<ProjectSummaryResponseModel> GetProjectSummaryAsync(int projectId, CancellationToken ct);
    Task<EarnedValueMetricsResponseModel> GetEarnedValueMetricsAsync(int projectId, CancellationToken ct);
}
