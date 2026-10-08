namespace Forge.Core.Models;

public record UpdateQcTemplateItemModel(
    int? Id,
    string Description,
    string? Specification,
    int SortOrder,
    bool IsRequired);
