namespace Forge.Core.Models;

public record CopqReportResponseModel
{
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public decimal InternalFailureCost { get; init; }
    public decimal ExternalFailureCost { get; init; }
    public decimal AppraisalCost { get; init; }
    public decimal PreventionCost { get; init; }
    public decimal TotalCopq { get; init; }
    public decimal Revenue { get; init; }
    public decimal CopqAsPercentOfRevenue { get; init; }
    public IReadOnlyList<CopqCategoryDetailResponseModel> Details { get; init; } = [];
    public IReadOnlyList<CopqTrendPointResponseModel> TrendData { get; init; } = [];
    public IReadOnlyList<CopqParetoItemResponseModel> ParetoByDefect { get; init; } = [];
}
