namespace Forge.Core.Models;

public record CreatePhysicalInventoryRequestModel
{
    public string? Description { get; init; }
    public int? ScopeLocationId { get; init; }
    public DateOnly CountDate { get; init; }
    public bool BlindCount { get; init; } = true;
    public decimal RecountTolerancePercent { get; init; }
    public string? Notes { get; init; }
}
