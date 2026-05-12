namespace Forge.Core.Models;

public record ReportEntityDefinitionModel(
    string EntitySource,
    string Label,
    List<ReportFieldDefinitionModel> Fields);
