namespace Forge.Core.Models;

public record WbsElementSummaryResponseModel
{
    public int Id { get; init; }
    public int? ParentElementId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public decimal BudgetTotal { get; init; }
    public decimal ActualTotal { get; init; }
    public decimal Variance { get; init; }
    public decimal? PercentComplete { get; init; }
    public IReadOnlyList<WbsElementSummaryResponseModel> Children { get; init; } = [];
}
