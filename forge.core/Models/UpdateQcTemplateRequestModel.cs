namespace Forge.Core.Models;

public record UpdateQcTemplateRequestModel(
    string Name,
    string? Description,
    int? PartId,
    List<UpdateQcTemplateItemModel> Items);
