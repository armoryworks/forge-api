using Microsoft.Extensions.Logging;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Integrations;

public class MockProjectAccountingService(ILogger<MockProjectAccountingService> logger) : IProjectAccountingService
{
    public Task<Project> CreateProjectAsync(CreateProjectRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] CreateProject {Name}", request.Name);
        var project = new Project
        {
            Id = 1,
            ProjectNumber = "PRJ-0001",
            Name = request.Name,
            BudgetTotal = request.BudgetTotal,
            Status = ProjectStatus.Planning,
        };
        return Task.FromResult(project);
    }

    public Task<Project> UpdateProjectAsync(int projectId, UpdateProjectRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] UpdateProject {ProjectId}", projectId);
        var project = new Project { Id = projectId, ProjectNumber = $"PRJ-{projectId:D4}" };
        return Task.FromResult(project);
    }

    public Task<WbsElement> AddWbsElementAsync(int projectId, CreateWbsElementRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] AddWbsElement to Project {ProjectId}, Code {Code}", projectId, request.Code);
        var element = new WbsElement
        {
            Id = 1,
            ProjectId = projectId,
            Code = request.Code,
            Name = request.Name,
            Type = request.Type,
        };
        return Task.FromResult(element);
    }

    public Task<WbsElement> UpdateWbsElementAsync(int elementId, UpdateWbsElementRequestModel request, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] UpdateWbsElement {ElementId}", elementId);
        var element = new WbsElement { Id = elementId };
        return Task.FromResult(element);
    }

    public Task DeleteWbsElementAsync(int elementId, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] DeleteWbsElement {ElementId}", elementId);
        return Task.CompletedTask;
    }

    public Task AddCostEntryAsync(int elementId, WbsCostEntry entry, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] AddCostEntry to Element {ElementId}, Amount {Amount}", elementId, entry.Amount);
        return Task.CompletedTask;
    }

    public Task RecalculateProjectTotalsAsync(int projectId, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] RecalculateProjectTotals {ProjectId}", projectId);
        return Task.CompletedTask;
    }

    public Task<ProjectSummaryResponseModel> GetProjectSummaryAsync(int projectId, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] GetProjectSummary {ProjectId}", projectId);
        return Task.FromResult(new ProjectSummaryResponseModel
        {
            ProjectId = projectId,
            BudgetTotal = 0,
            ActualTotal = 0,
            CommittedTotal = 0,
            EstimateAtCompletion = 0,
            VarianceAtCompletion = 0,
            PercentComplete = 0,
            WbsTree = [],
        });
    }

    public Task<EarnedValueMetricsResponseModel> GetEarnedValueMetricsAsync(int projectId, CancellationToken ct)
    {
        logger.LogInformation("[MockProjectAccounting] GetEarnedValueMetrics {ProjectId}", projectId);
        return Task.FromResult(new EarnedValueMetricsResponseModel
        {
            BudgetedCostOfWorkScheduled = 0,
            BudgetedCostOfWorkPerformed = 0,
            ActualCostOfWorkPerformed = 0,
            ScheduleVariance = 0,
            CostVariance = 0,
            SchedulePerformanceIndex = 1,
            CostPerformanceIndex = 1,
            EstimateAtCompletion = 0,
            EstimateToComplete = 0,
        });
    }
}
