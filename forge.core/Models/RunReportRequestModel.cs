namespace Forge.Core.Models;

public record RunReportRequestModel(
    string EntitySource,
    string[] Columns,
    ReportFilterModel[]? Filters,
    string? GroupByField,
    string? SortField,
    string? SortDirection,
    int? Page,
    int? PageSize);
