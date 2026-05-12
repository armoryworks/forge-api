namespace Forge.Core.Models;

public record CreateQcTemplateItemModel(
    string Description,
    string? Specification,
    int SortOrder,
    bool IsRequired);
