namespace Forge.Core.Models.Costing;

/// <summary>Whole-graph costing-template save: create (null id) or replace (id).</summary>
public sealed record SaveCostingTemplateRequestModel
{
    public int? Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<SaveCostingTemplateLineModel> Lines { get; init; }
}
