using Forge.Core.Enums;

namespace Forge.Core.Entities;

public class WbsElement : BaseAuditableEntity
{
    public int ProjectId { get; set; }
    public int? ParentElementId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WbsElementType Type { get; set; } = WbsElementType.WorkPackage;
    public decimal BudgetLabor { get; set; }
    public decimal BudgetMaterial { get; set; }
    public decimal BudgetOther { get; set; }
    public decimal BudgetTotal { get; set; }
    public decimal ActualLabor { get; set; }
    public decimal ActualMaterial { get; set; }
    public decimal ActualOther { get; set; }
    public decimal ActualTotal { get; set; }
    public int SortOrder { get; set; }
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public decimal? PercentComplete { get; set; }

    public Project Project { get; set; } = null!;
    public WbsElement? ParentElement { get; set; }
    public ICollection<WbsElement> ChildElements { get; set; } = [];
    public ICollection<WbsCostEntry> CostEntries { get; set; } = [];
}
