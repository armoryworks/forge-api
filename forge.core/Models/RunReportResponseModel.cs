namespace Forge.Core.Models;

public record RunReportResponseModel(
    string[] Columns,
    List<Dictionary<string, object?>> Rows,
    int TotalCount,
    Dictionary<string, List<Dictionary<string, object?>>>? GroupedData);
