using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateWbsElementRequestModel
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public WbsElementType? Type { get; init; }
    public decimal? BudgetLabor { get; init; }
    public decimal? BudgetMaterial { get; init; }
    public decimal? BudgetOther { get; init; }
    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedEnd { get; init; }
    public decimal? PercentComplete { get; init; }
    public int? SortOrder { get; init; }
}
