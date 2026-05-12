namespace Forge.Core.Models;

public record UpdateSavedReportRequestModel(
    string Name,
    string? Description,
    string EntitySource,
    string[] Columns,
    ReportFilterModel[]? Filters,
    string? GroupByField,
    string? SortField,
    string? SortDirection,
    string? ChartType,
    string? ChartLabelField,
    string? ChartValueField,
    bool IsShared);
