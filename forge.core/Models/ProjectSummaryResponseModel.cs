namespace Forge.Core.Models;

public record ProjectSummaryResponseModel
{
    public int ProjectId { get; init; }
    public decimal BudgetTotal { get; init; }
    public decimal ActualTotal { get; init; }
    public decimal CommittedTotal { get; init; }
    public decimal EstimateAtCompletion { get; init; }
    public decimal VarianceAtCompletion { get; init; }
    public decimal PercentComplete { get; init; }
    public IReadOnlyList<WbsElementSummaryResponseModel> WbsTree { get; init; } = [];
}
