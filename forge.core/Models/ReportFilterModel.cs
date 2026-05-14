
namespace Forge.Core.Models;

public record ReportFilterModel(
    string Field,
    ReportFilterOperator Operator,
    string? Value = null,
    string? Value2 = null);
