namespace Forge.Core.Entities;

public class SavedReport : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EntitySource { get; set; } = string.Empty;
    public string ColumnsJson { get; set; } = "[]";
    public string? FiltersJson { get; set; }
    public string? GroupByField { get; set; }
    public string? SortField { get; set; }
    public string? SortDirection { get; set; }
    public string? ChartType { get; set; }
    public string? ChartLabelField { get; set; }
    public string? ChartValueField { get; set; }
    public bool IsShared { get; set; }
    public int UserId { get; set; }
}
