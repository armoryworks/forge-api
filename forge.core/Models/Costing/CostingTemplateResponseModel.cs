namespace Forge.Core.Models.Costing;

/// <summary>A costing template with its overhead-category lines.</summary>
public record CostingTemplateResponseModel(
    int Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<CostingTemplateLineModel> Lines);
