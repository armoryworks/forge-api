namespace Forge.Core.Models;

public record CreateMaterialSpecRequestModel(
    string Label,
    int? ParentId = null,
    string? NewCategoryLabel = null);
