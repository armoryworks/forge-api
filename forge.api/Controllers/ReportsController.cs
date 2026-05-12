using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Reports;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-RPT-OPERATIONAL")]
public class ReportsController(IMediator mediator) : ControllerBase
{
    [HttpGet("jobs-by-stage")]
    public async Task<ActionResult<List<JobsByStageReportItem>>> GetJobsByStage([FromQuery] int? trackTypeId)
    {
        var result = await mediator.Send(new GetJobsByStageReportQuery(trackTypeId));
        return Ok(result);
    }

    [HttpGet("overdue-jobs")]
    public async Task<ActionResult<List<OverdueJobReportItem>>> GetOverdueJobs()
    {
        var result = await mediator.Send(new GetOverdueJobsReportQuery());
        return Ok(result);
    }

    [HttpGet("time-by-user")]
    public async Task<ActionResult<List<TimeByUserReportItem>>> GetTimeByUser(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetTimeByUserReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("expense-summary")]
    public async Task<ActionResult<List<ExpenseSummaryReportItem>>> GetExpenseSummary(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetExpenseSummaryReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("lead-pipeline")]
    public async Task<ActionResult<List<LeadPipelineReportItem>>> GetLeadPipeline()
    {
        var result = await mediator.Send(new GetLeadPipelineReportQuery());
        return Ok(result);
    }

    [HttpGet("job-completion-trend")]
    public async Task<ActionResult<List<JobCompletionTrendItem>>> GetJobCompletionTrend([FromQuery] int months = 6)
    {
        var result = await mediator.Send(new GetJobCompletionTrendQuery(months));
        return Ok(result);
    }

    [HttpGet("on-time-delivery")]
    public async Task<ActionResult<OnTimeDeliveryReportItem>> GetOnTimeDelivery(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetOnTimeDeliveryReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("average-lead-time")]
    public async Task<ActionResult<List<AverageLeadTimeReportItem>>> GetAverageLeadTime()
    {
        var result = await mediator.Send(new GetAverageLeadTimeReportQuery());
        return Ok(result);
    }

    [HttpGet("team-workload")]
    public async Task<ActionResult<List<TeamWorkloadReportItem>>> GetTeamWorkload()
    {
        var result = await mediator.Send(new GetTeamWorkloadReportQuery());
        return Ok(result);
    }

    [HttpGet("customer-activity")]
    public async Task<ActionResult<List<CustomerActivityReportItem>>> GetCustomerActivity()
    {
        var result = await mediator.Send(new GetCustomerActivityReportQuery());
        return Ok(result);
    }

    [HttpGet("my-work-history")]
    public async Task<ActionResult<List<MyWorkHistoryReportItem>>> GetMyWorkHistory()
    {
        var result = await mediator.Send(new GetMyWorkHistoryQuery());
        return Ok(result);
    }

    [HttpGet("my-time-log")]
    public async Task<ActionResult<List<MyTimeLogReportItem>>> GetMyTimeLog(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetMyTimeLogQuery(start, end));
        return Ok(result);
    }

    // ─── Financial Reports ───

    [HttpGet("ar-aging")]
    public async Task<ActionResult<List<ArAgingReportItem>>> GetArAging()
    {
        var result = await mediator.Send(new GetArAgingReportQuery());
        return Ok(result);
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<List<RevenueReportItem>>> GetRevenue(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end,
        [FromQuery] string groupBy = "period")
    {
        var result = await mediator.Send(new GetRevenueReportQuery(start, end, groupBy));
        return Ok(result);
    }

    [HttpGet("simple-pnl")]
    public async Task<ActionResult<List<SimplePnlReportItem>>> GetSimplePnl(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetSimplePnlReportQuery(start, end));
        return Ok(result);
    }

    // ─── Additional Reports ───

    [HttpGet("my-expense-history")]
    public async Task<ActionResult<List<MyExpenseHistoryReportItem>>> GetMyExpenseHistory(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetMyExpenseHistoryQuery(start, end));
        return Ok(result);
    }

    [HttpGet("quote-to-close")]
    public async Task<ActionResult<List<QuoteToCloseReportItem>>> GetQuoteToClose(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        // WU-15 / Phase 3 / RPT-QUOTECONV-001: when no range is supplied,
        // default to a trailing 12-month window so the report aggregates
        // sensibly rather than returning empty (the previous behavior bound
        // missing params to DateTimeOffset.MinValue, filtering out every row).
        var (s, e) = ResolveDefaultDateRange(start, end);
        var result = await mediator.Send(new GetQuoteToCloseReportQuery(s, e));
        return Ok(result);
    }

    /// <summary>
    /// Phase 1r / Batch 16 — win/loss by RFQ part-class. Optional date range
    /// scopes the cohort; omitted = all-time. Capability gated under
    /// CAP-O2C-LEAD since the part-class taxonomy is a lead-intake field.
    /// </summary>
    [HttpGet("win-loss-by-class")]
    [RequiresCapability("CAP-O2C-LEAD")]
    public async Task<ActionResult<List<WinLossByClassReportItem>>> GetWinLossByClass(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetWinLossByClassReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("shipping-summary")]
    public async Task<ActionResult<List<ShippingSummaryReportItem>>> GetShippingSummary(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetShippingSummaryReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("time-in-stage")]
    public async Task<ActionResult<List<TimeInStageReportItem>>> GetTimeInStage([FromQuery] int? trackTypeId)
    {
        var result = await mediator.Send(new GetTimeInStageReportQuery(trackTypeId));
        return Ok(result);
    }

    // ─── Batch 4 Reports ───

    [HttpGet("employee-productivity")]
    public async Task<ActionResult<List<EmployeeProductivityReportItem>>> GetEmployeeProductivity(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        // WU-15 / Phase 3 / RPT-EMPLABOR-001: when no range is supplied,
        // default to a trailing 12-month window so the report aggregates
        // against the existing time-entry rows. Previously, missing params
        // bound to DateTimeOffset.MinValue and the date filter excluded
        // everything.
        var (s, e) = ResolveDefaultDateRange(start, end);
        var result = await mediator.Send(new GetEmployeeProductivityReportQuery(s, e));
        return Ok(result);
    }

    [HttpGet("inventory-levels")]
    public async Task<ActionResult<List<InventoryLevelReportItem>>> GetInventoryLevels()
    {
        var result = await mediator.Send(new GetInventoryLevelsReportQuery());
        return Ok(result);
    }

    [HttpGet("maintenance")]
    public async Task<ActionResult<List<MaintenanceReportItem>>> GetMaintenance(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetMaintenanceReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("quality-scrap")]
    public async Task<ActionResult<List<QualityScrapReportItem>>> GetQualityScrap(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetQualityScrapReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("cycle-review")]
    public async Task<ActionResult<List<CycleReviewReportItem>>> GetCycleReview()
    {
        var result = await mediator.Send(new GetCycleReviewReportQuery());
        return Ok(result);
    }

    [HttpGet("job-margin")]
    public async Task<ActionResult<List<JobMarginReportItem>>> GetJobMargin(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetJobMarginReportQuery(start, end));
        return Ok(result);
    }

    // ─── Batch 5 Reports ───

    [HttpGet("my-cycle-summary")]
    public async Task<ActionResult<List<MyCycleSummaryReportItem>>> GetMyCycleSummary()
    {
        var result = await mediator.Send(new GetMyCycleSummaryReportQuery());
        return Ok(result);
    }

    [HttpGet("lead-sales")]
    public async Task<ActionResult<LeadSalesReportItem>> GetLeadSales(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetLeadSalesReportQuery(start, end));
        return Ok(result);
    }

    [HttpGet("rd")]
    public async Task<ActionResult<List<RdReportItem>>> GetRdReport(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetRdReportQuery(start, end));
        return Ok(result);
    }

    // ─── Operation Time Reports ───

    [HttpGet("time-by-operation")]
    public async Task<ActionResult<List<TimeByOperationReportRow>>> GetTimeByOperation(
        [FromQuery] int? partId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct)
    {
        var result = await mediator.Send(new GetTimeByOperationReportQuery(partId, dateFrom, dateTo), ct);
        return Ok(result);
    }

    // ─── Job Costing Reports ───

    [HttpGet("job-profitability")]
    public async Task<ActionResult<List<JobProfitabilityReportRow>>> GetJobProfitability(
        [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo,
        [FromQuery] int? customerId, [FromQuery] decimal? minMargin, CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetJobProfitabilityReportQuery(dateFrom, dateTo, customerId, minMargin), ct);
        return Ok(result);
    }

    // ─── OEE Reports ───

    [HttpGet("oee")]
    public async Task<ActionResult<IReadOnlyList<OeeCalculationModel>>> GetOeeReport(
        [FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        var result = await mediator.Send(new GetOeeReportQuery(dateFrom, dateTo), ct);
        return Ok(result);
    }

    [HttpGet("oee/{workCenterId:int}")]
    public async Task<ActionResult<OeeCalculationModel>> GetOeeByWorkCenter(
        int workCenterId, [FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        var result = await mediator.Send(new GetOeeByWorkCenterQuery(workCenterId, dateFrom, dateTo), ct);
        return Ok(result);
    }

    [HttpGet("oee/{workCenterId:int}/trend")]
    public async Task<ActionResult<IReadOnlyList<OeeTrendPointModel>>> GetOeeTrend(
        int workCenterId, [FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo,
        [FromQuery] OeeTrendGranularity granularity = OeeTrendGranularity.Daily, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetOeeTrendQuery(workCenterId, dateFrom, dateTo, granularity), ct);
        return Ok(result);
    }

    [HttpGet("oee/{workCenterId:int}/losses")]
    public async Task<ActionResult<SixBigLossesModel>> GetSixBigLosses(
        int workCenterId, [FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSixBigLossesQuery(workCenterId, dateFrom, dateTo), ct);
        return Ok(result);
    }

    // WU-15 / Phase 3 / H1: shared helper for date-ranged reports that should
    // aggregate against the trailing 12 months when no explicit range is
    // supplied (rather than DateTimeOffset.MinValue, which yields an empty
    // result set on every date filter).
    private static (DateTimeOffset Start, DateTimeOffset End) ResolveDefaultDateRange(
        DateTimeOffset? start, DateTimeOffset? end)
    {
        var resolvedEnd = end ?? DateTimeOffset.UtcNow;
        var resolvedStart = start ?? resolvedEnd.AddMonths(-12);
        return (resolvedStart, resolvedEnd);
    }
}
