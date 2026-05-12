namespace Forge.Core.Models;

public record CustomFieldDefinitionModel(
    string Key,
    string Label,
    string Type,
    bool IsRequired,
    string[]? Options);
