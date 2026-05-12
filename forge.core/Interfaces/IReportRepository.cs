using Forge.Core.Models;

namespace Forge.Core.Interfaces;

public interface IReportRepository
{
    Task<List<JobsByStageReportItem>> GetJobsByStageAsync(int? trackTypeId, CancellationToken ct);
    Task<List<OverdueJobReportItem>> GetOverdueJobsAsync(CancellationToken ct);
    Task<List<TimeByUserReportItem>> GetTimeByUserAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<ExpenseSummaryReportItem>> GetExpenseSummaryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<LeadPipelineReportItem>> GetLeadPipelineAsync(CancellationToken ct);
    Task<List<JobCompletionTrendItem>> GetJobCompletionTrendAsync(int months, CancellationToken ct);
    Task<OnTimeDeliveryReportItem> GetOnTimeDeliveryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<AverageLeadTimeReportItem>> GetAverageLeadTimeAsync(CancellationToken ct);
    Task<List<TeamWorkloadReportItem>> GetTeamWorkloadAsync(CancellationToken ct);
    Task<List<CustomerActivityReportItem>> GetCustomerActivityAsync(CancellationToken ct);
    Task<List<MyWorkHistoryReportItem>> GetMyWorkHistoryAsync(int userId, CancellationToken ct);
    Task<List<MyTimeLogReportItem>> GetMyTimeLogAsync(int userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    // Financial reports
    Task<List<ArAgingReportItem>> GetArAgingAsync(CancellationToken ct);
    Task<List<RevenueReportItem>> GetRevenueAsync(DateTimeOffset start, DateTimeOffset end, string groupBy, CancellationToken ct);
    Task<List<SimplePnlReportItem>> GetSimplePnlAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    // Additional reports
    Task<List<MyExpenseHistoryReportItem>> GetMyExpenseHistoryAsync(int userId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<QuoteToCloseReportItem>> GetQuoteToCloseAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<ShippingSummaryReportItem>> GetShippingSummaryAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<TimeInStageReportItem>> GetTimeInStageAsync(int? trackTypeId, CancellationToken ct);

    // Batch 4 reports
    Task<List<EmployeeProductivityReportItem>> GetEmployeeProductivityAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<InventoryLevelReportItem>> GetInventoryLevelsAsync(CancellationToken ct);
    Task<List<MaintenanceReportItem>> GetMaintenanceAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<QualityScrapReportItem>> GetQualityScrapAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
    Task<List<CycleReviewReportItem>> GetCycleReviewAsync(CancellationToken ct);
}
