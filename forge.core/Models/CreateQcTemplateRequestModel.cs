namespace Forge.Core.Models;

public record CreateQcTemplateRequestModel(
    string Name,
    string? Description,
    int? PartId,
    List<CreateQcTemplateItemModel> Items);
