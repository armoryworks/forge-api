namespace Forge.Core.Models;

public record ReportFieldDefinitionModel(
    string Field,
    string Label,
    string Type,
    bool IsFilterable,
    bool IsSortable,
    bool IsGroupable);
