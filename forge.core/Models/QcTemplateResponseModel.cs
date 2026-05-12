namespace Forge.Core.Models;

public record QcTemplateResponseModel(
    int Id,
    string Name,
    string? Description,
    int? PartId,
    string? PartNumber,
    bool IsActive,
    List<QcTemplateItemModel> Items);
