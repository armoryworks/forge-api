
namespace Forge.Core.Models;

public record CreateWbsElementRequestModel
{
    public int? ParentElementId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public WbsElementType Type { get; init; } = WbsElementType.WorkPackage;
    public decimal BudgetLabor { get; init; }
    public decimal BudgetMaterial { get; init; }
    public decimal BudgetOther { get; init; }
    public DateOnly? PlannedStart { get; init; }
    public DateOnly? PlannedEnd { get; init; }
    public int SortOrder { get; init; }
}
